using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.Antimatter;

/// <summary>
/// <see cref="IPaymentProvider"/> for Antimatter Gateway
/// (antimatter.profit-gateway.com), hosted-page checkout in EUR/RUB.
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="antimatter":
///   ApiKey   -> x-api-key header value (ak_... from the gateway LK)
///   Settings -> optional JSON {"baseUrl":"...","refererDomain":"https://key-zona.com"}
///
/// Auth: x-api-key header + Referer whitelist (no request signing).
/// Webhook auth: sign = md5(paymentId + timestamp + apiKey), see
/// <see cref="AntimatterSignatureHelper"/>.
///
/// No status-check or refund endpoint is documented — GetPaymentStatusAsync
/// reports our locally stored status only; the real source of truth is the
/// webhook.
/// </summary>
public class AntimatterPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl = "https://antimatter.profit-gateway.com/api/v1";

    // Their docs show "Referer: https://ваш-домен.com", but the gateway admin
    // says that's wrong ("дезинформация в инструкции") and the header must be
    // the bare domain. Probing both forms gives an identical result today —
    // "key-zona.com" and "https://key-zona.com/" both clear the whitelist and
    // fail later at the submerchant lookup — while "key-zona.com/" (bare with
    // a trailing slash) is rejected outright with 403 "Referer not
    // whitelisted". Sending the bare form per their instruction.
    private const string DefaultRefererDomain = "key-zona.com";
    private const string HttpClientName = "antimatter";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<AntimatterPaymentProvider> _log;

    public AntimatterPaymentProvider(
        ApplicationDbContext db,
        IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer,
        ILogger<AntimatterPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "antimatter";
    public string DisplayName => "Antimatter";

    public IReadOnlyList<PaymentMethod> SupportedMethods => new[]
    {
        PaymentMethod.SBP,
    };

    public bool IsEnabled
    {
        get
        {
            var cfg = _db.PaymentProviderConfigs.AsNoTracking()
                .FirstOrDefault(c => c.Name == Name);
            return cfg?.IsEnabled == true && !string.IsNullOrWhiteSpace(cfg.ApiKey);
        }
    }

    public bool SupportsRefund => false;

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("Antimatter не настроена в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("Antimatter временно отключена.");
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            return PaymentResult.Failed("Antimatter: не задан API-ключ.");

        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var currency = NormalizeCurrency(request.Currency);

        var contacts = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);

        var body = new Dictionary<string, object?>
        {
            ["amount"] = amount,
            ["currency"] = currency,
            ["orderId"] = ourTransactionId,
            ["description"] = request.Description ?? $"Оплата #{ourTransactionId}",
            ["clientEmail"] = contacts.Email,
        };

        // Per docs: EUR uses a single redirect_url (gateway appends
        // ?state=success|fail); RUB uses separate successUrl/failUrl.
        if (currency == "EUR")
        {
            body["redirect_url"] = request.SuccessUrl;
        }
        else
        {
            body["successUrl"] = request.SuccessUrl;
            body["failUrl"] = request.CancelUrl ?? request.SuccessUrl;
        }

        var bodyJson = JsonSerializer.Serialize(body, JsonOptions);
        var url = ReadBaseUrl(cfg) + "/payment/create";

        try
        {
            _log.LogInformation(
                "Antimatter → POST {Url} txn={Txn} amount={Amount} {Currency}",
                url, ourTransactionId, amount, currency);

            using var http = _httpFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
            };
            httpRequest.Headers.Remove("x-api-key");
            httpRequest.Headers.Add("x-api-key", cfg.ApiKey);
            SetRefererHeader(httpRequest, ReadRefererDomain(cfg));

            using var resp = await http.SendAsync(httpRequest, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            _log.LogInformation("Antimatter ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
            {
                return PaymentResult.Failed(
                    $"Antimatter вернула HTTP {(int)resp.StatusCode}: {raw}",
                    ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var status = ReadString(root, "status");
            // Antimatter may acknowledge creation with { success: true, status: "PENDING" }
            // while the older response format used status: "success".
            var successFlag = root.TryGetProperty("success", out var successElement)
                && successElement.ValueKind == JsonValueKind.True;
            if (!successFlag && !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                var errMsg = ReadString(root, "message") ?? ReadString(root, "error") ?? raw;
                return PaymentResult.Failed($"Antimatter отказала в создании платежа: {errMsg}");
            }

            var paymentId = ReadString(root, "paymentId");
            var paymentUrl = ReadString(root, "paymentUrl");

            if (string.IsNullOrEmpty(paymentId))
                return PaymentResult.Failed($"Antimatter не вернула paymentId: {raw}");
            if (string.IsNullOrEmpty(paymentUrl))
                return PaymentResult.Failed($"Antimatter не вернула ссылку оплаты: {raw}");

            var result = PaymentResult.Successful(ourTransactionId, paymentUrl, paymentId);
            result.SentContacts = new SentContactData(
                Email: contacts.Email,
                Phone: contacts.Phone,
                Name: contacts.Name,
                UserId: null,
                Ip: contacts.Ip,
                Anonymized: contacts.Anonymized);
            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Antimatter /payment/create failed");
            return PaymentResult.Failed($"Antimatter недоступна: {ex.Message}");
        }
    }

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        // No status-check endpoint is documented for this gateway — the
        // webhook is the only source of truth. Report our locally stored
        // status instead of guessing via an undocumented call.
        var tx = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ProviderName == Name &&
                (t.TransactionId == transactionId || t.ProviderTransactionId == transactionId), ct);

        return new PaymentStatusResult
        {
            TransactionId = transactionId,
            Status = tx?.Status ?? PaymentStatus.Pending,
            Amount = tx?.Amount ?? 0,
            Currency = tx?.Currency ?? "RUB",
            UpdatedAt = tx?.UpdatedAt ?? DateTime.UtcNow,
            Message = "Antimatter не документирует API статуса — источник истины: webhook.",
        };
    }

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers,
        string body,
        CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.ApiKey))
            return WebhookValidationResult.Invalid("Antimatter not configured.");

        if (string.IsNullOrWhiteSpace(body))
            return WebhookValidationResult.Invalid("Antimatter webhook: empty body.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            return WebhookValidationResult.Invalid($"Antimatter webhook: body is not JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            var paymentId = ReadString(root, "paymentId");
            var timestamp = ReadString(root, "timestamp");
            var receivedSign = ReadString(root, "sign");

            if (string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(receivedSign))
                return WebhookValidationResult.Invalid("Antimatter webhook: missing paymentId/timestamp/sign.");

            var expectedSign = AntimatterSignatureHelper.BuildSignature(paymentId, timestamp, cfg.ApiKey!);
            if (!AntimatterSignatureHelper.FixedTimeEquals(receivedSign, expectedSign))
            {
                _log.LogWarning("Antimatter webhook signature mismatch for paymentId {PaymentId}", paymentId);
                return WebhookValidationResult.Invalid("Antimatter webhook: signature mismatch.");
            }

            var orderId = ReadString(root, "orderId");
            var txnId = !string.IsNullOrEmpty(orderId) ? orderId : paymentId;

            var status = ReadString(root, "status");
            var amount = ReadDecimal(root, "amount");
            var failReason = ReadString(root, "failReason");

            var mappedStatus = MapStatus(status);
            if (mappedStatus == null)
            {
                _log.LogWarning("Antimatter webhook: unrecognised status '{Status}' for paymentId {PaymentId}", status, paymentId);
                return WebhookValidationResult.Invalid($"Antimatter webhook: unrecognised status '{status}'.");
            }

            return new WebhookValidationResult
            {
                IsValid = true,
                TransactionId = txnId,
                NewStatus = mappedStatus,
                Amount = amount,
                RawData = body,
                ErrorMessage = failReason,
            };
        }
    }

    public Task<PaymentResult> RefundAsync(
        string transactionId,
        decimal? amount = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(PaymentResult.Failed(
            "Возвраты через Antimatter API не документированы — выполните возврат вручную в ЛК Antimatter."));
    }

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    private static PaymentStatus? MapStatus(string? status) => (status ?? "").Trim().ToUpperInvariant() switch
    {
        "COMPLETED" => PaymentStatus.Completed,
        "FAILED" => PaymentStatus.Failed,
        _ => null,
    };

    private static string NormalizeCurrency(string? currency)
    {
        var c = (currency ?? "RUB").Trim().ToUpperInvariant();
        return c is "EUR" or "RUB" ? c : "RUB";
    }

    private static string ReadBaseUrl(PaymentProviderConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Settings))
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg.Settings);
                if (doc.RootElement.TryGetProperty("baseUrl", out var b))
                {
                    var value = b.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value.TrimEnd('/');
                }
            }
            catch { /* malformed Settings - fall back to default */ }
        }

        return DefaultBaseUrl;
    }

    /// <summary>
    /// Sets the Referer header, accepting either a bare domain
    /// ("key-zona.com") or a full URL ("https://key-zona.com").
    /// <see cref="HttpRequestHeaders.Referrer"/> only takes an absolute URI,
    /// so the bare form has to bypass validation — otherwise constructing the
    /// Uri throws and every payment fails.
    /// </summary>
    private static void SetRefererHeader(HttpRequestMessage request, string referer)
    {
        if (Uri.TryCreate(referer, UriKind.Absolute, out var absolute))
        {
            request.Headers.Referrer = absolute;
            return;
        }

        request.Headers.TryAddWithoutValidation("Referer", referer);
    }

    private static string ReadRefererDomain(PaymentProviderConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Settings))
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg.Settings);
                if (doc.RootElement.TryGetProperty("refererDomain", out var r))
                {
                    var value = r.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            catch { /* malformed Settings - fall back to default */ }
        }

        return DefaultRefererDomain;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    private static decimal ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out var n) ? n : 0,
            JsonValueKind.String => decimal.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var s) ? s : 0,
            _ => 0,
        };
    }
}
