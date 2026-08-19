using System.Globalization;
using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.RollyPay;

/// <summary>
/// <see cref="IPaymentProvider"/> for RollyPay (API on api.rollypay.io, hosted
/// checkout served from pay.rollypay.io) — RUB in / USDT settlement. Wired for
/// SBP only, per the merchant account we were given ("СБП нужен ток").
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="rollypay":
///   ApiKey     -> X-API-Key header (issued with the cashbox/terminal)
///   SecretKey  -> signing_secret, used ONLY to verify webhook signatures
///   MerchantId -> terminal_id (cashbox UUID); optional when authenticating
///                 by API key, sent when present for explicitness
///   Settings   -> optional JSON {"baseUrl":"https://api.rollypay.io/api/v1"}
///
/// Auth: X-API-Key header + X-Nonce (unique per request, 10-minute validity).
/// Outbound requests are NOT signed — only webhooks are.
///
/// Webhook: POST /api/webhooks/rollypay, verified as
/// hex(HMAC-SHA256(signing_secret, X-Timestamp + "." + rawBody)) against the
/// X-Signature header. See <see cref="RollyPaySignatureHelper"/>.
///
/// IMPORTANT (setup step outside the code): the callback URL is not part of
/// the create-payment call — it lives on the terminal and must be set once,
/// either in the panel or via PUT /api/v1/terminals/{terminalId}
/// {"callback_url":"https://key-zona.com/api/webhooks/rollypay"}. Without it
/// no payment ever gets confirmed automatically.
/// </summary>
public class RollyPayPaymentProvider : IPaymentProvider
{
    // API host is api.rollypay.io — confirmed by the cashbox's own "Быстрый
    // старт" snippet in the panel. pay.rollypay.io is only where the returned
    // pay_url (hosted checkout page) lives, NOT the API.
    private const string DefaultBaseUrl = "https://api.rollypay.io/api/v1";
    private const string HttpClientName = "rollypay";

    /// <summary>Payment method code expected by RollyPay for Faster Payments.</summary>
    private const string SbpMethodCode = "sbp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<RollyPayPaymentProvider> _log;

    public RollyPayPaymentProvider(
        ApplicationDbContext db,
        IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer,
        ILogger<RollyPayPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "rollypay";
    public string DisplayName => "RollyPay";

    // SBP only — the cashbox also exposes crypto/cryptobot/xrocket, but those
    // are deliberately not offered on the storefront.
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
        if (cfg == null) return PaymentResult.Failed("RollyPay не настроена в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("RollyPay временно отключена.");
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            return PaymentResult.Failed("RollyPay: не задан API-ключ.");

        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);
        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);

        var contacts = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);

        var body = new Dictionary<string, object?>
        {
            // Docs type `amount` as a string with 2 decimals ("1500.00").
            ["amount"] = amount.ToString("F2", CultureInfo.InvariantCulture),
            ["payment_currency"] = "RUB",
            ["payment_method"] = SbpMethodCode,
            ["order_id"] = ourTransactionId,
            ["description"] = request.Description ?? $"Оплата #{ourTransactionId}",
            ["success_redirect_url"] = request.SuccessUrl,
            ["fail_redirect_url"] = request.CancelUrl ?? request.SuccessUrl,
        };

        // Optional when authenticating by API key, but harmless and explicit.
        if (!string.IsNullOrWhiteSpace(cfg.MerchantId))
            body["terminal_id"] = cfg.MerchantId;

        if (cfg.IsTestMode)
            body["test"] = true;

        var bodyJson = JsonSerializer.Serialize(body, JsonOptions);
        var url = ReadBaseUrl(cfg) + "/payments";

        try
        {
            _log.LogInformation(
                "RollyPay → POST {Url} txn={Txn} amount={Amount} RUB method={Method}",
                url, ourTransactionId, amount, SbpMethodCode);

            using var http = _httpFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
            };
            httpRequest.Headers.TryAddWithoutValidation("X-API-Key", cfg.ApiKey);
            // Unique per request, 10-minute validity per docs.
            httpRequest.Headers.TryAddWithoutValidation("X-Nonce", Guid.NewGuid().ToString("N"));

            using var resp = await http.SendAsync(httpRequest, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            _log.LogInformation("RollyPay ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
            {
                return PaymentResult.Failed(
                    $"RollyPay вернула HTTP {(int)resp.StatusCode}: {raw}",
                    ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var paymentId = ReadString(root, "payment_id");
            var payUrl = ReadString(root, "pay_url");

            if (string.IsNullOrEmpty(paymentId))
                return PaymentResult.Failed($"RollyPay не вернула payment_id: {raw}");
            if (string.IsNullOrEmpty(payUrl))
                return PaymentResult.Failed($"RollyPay не вернула ссылку оплаты (pay_url): {raw}");

            var result = PaymentResult.Successful(ourTransactionId, payUrl, paymentId);
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
            _log.LogError(ex, "RollyPay /payments failed");
            return PaymentResult.Failed($"RollyPay недоступна: {ex.Message}");
        }
    }

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);

        // The status endpoint is keyed by RollyPay's own payment_id, so resolve
        // our transaction id to the provider id first.
        var tx = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ProviderName == Name &&
                (t.TransactionId == transactionId || t.ProviderTransactionId == transactionId), ct);

        var providerId = tx?.ProviderTransactionId ?? transactionId;

        var fallback = new PaymentStatusResult
        {
            TransactionId = transactionId,
            Status = tx?.Status ?? PaymentStatus.Pending,
            Amount = tx?.Amount ?? 0,
            Currency = tx?.Currency ?? "RUB",
            UpdatedAt = tx?.UpdatedAt ?? DateTime.UtcNow,
        };

        if (cfg == null || string.IsNullOrWhiteSpace(cfg.ApiKey) || string.IsNullOrWhiteSpace(providerId))
        {
            fallback.Message = "RollyPay не настроена — показан локальный статус.";
            return fallback;
        }

        try
        {
            var url = $"{ReadBaseUrl(cfg)}/payments/{Uri.EscapeDataString(providerId)}";

            using var http = _httpFactory.CreateClient(HttpClientName);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.TryAddWithoutValidation("X-API-Key", cfg.ApiKey);
            httpRequest.Headers.TryAddWithoutValidation("X-Nonce", Guid.NewGuid().ToString("N"));

            using var resp = await http.SendAsync(httpRequest, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("RollyPay status ← {Status} {Body}", (int)resp.StatusCode, raw);
                fallback.Message = $"RollyPay статус недоступен (HTTP {(int)resp.StatusCode}) — показан локальный.";
                return fallback;
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var mapped = MapStatus(ReadString(root, "status"));

            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status = mapped ?? fallback.Status,
                Amount = ReadDecimal(root, "amount"),
                Currency = ReadString(root, "payment_currency") ?? "RUB",
                UpdatedAt = DateTime.UtcNow,
                Message = ReadString(root, "status"),
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RollyPay status check failed for {TransactionId}", transactionId);
            fallback.Message = $"RollyPay недоступна: {ex.Message}";
            return fallback;
        }
    }

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers,
        string body,
        CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.SecretKey))
            return WebhookValidationResult.Invalid("RollyPay: не задан signing_secret (SecretKey).");

        if (string.IsNullOrWhiteSpace(body))
            return WebhookValidationResult.Invalid("RollyPay webhook: empty body.");

        var signature = ReadHeader(headers, "X-Signature");
        var timestamp = ReadHeader(headers, "X-Timestamp");

        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp))
            return WebhookValidationResult.Invalid("RollyPay webhook: missing X-Signature/X-Timestamp.");

        var expected = RollyPaySignatureHelper.BuildSignature(timestamp, body, cfg.SecretKey!);
        if (!RollyPaySignatureHelper.FixedTimeEquals(signature, expected))
        {
            _log.LogWarning("RollyPay webhook signature mismatch (ts={Timestamp})", timestamp);
            return WebhookValidationResult.Invalid("RollyPay webhook: signature mismatch.");
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            return WebhookValidationResult.Invalid($"RollyPay webhook: body is not JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;

            var orderId = ReadString(root, "order_id");
            var paymentId = ReadString(root, "payment_id");
            var txnId = !string.IsNullOrEmpty(orderId) ? orderId : paymentId;

            if (string.IsNullOrEmpty(txnId))
                return WebhookValidationResult.Invalid("RollyPay webhook: missing order_id/payment_id.");

            var status = ReadString(root, "status");
            var eventType = ReadString(root, "event_type");
            var mapped = MapStatus(status);

            if (mapped == null)
            {
                _log.LogWarning(
                    "RollyPay webhook: unrecognised status '{Status}' (event {Event}) for {TxnId}",
                    status, eventType, txnId);
                return WebhookValidationResult.Invalid($"RollyPay webhook: unrecognised status '{status}'.");
            }

            // Sandbox payments must never move real balances.
            if (root.TryGetProperty("test", out var testEl) && testEl.ValueKind == JsonValueKind.True)
            {
                _log.LogWarning("RollyPay webhook: TEST payment {TxnId} ignored (event {Event}).", txnId, eventType);
                return WebhookValidationResult.Invalid("RollyPay webhook: test payment ignored.");
            }

            return new WebhookValidationResult
            {
                IsValid = true,
                TransactionId = txnId,
                NewStatus = mapped,
                Amount = ReadDecimal(root, "amount"),
                RawData = body,
            };
        }
    }

    public Task<PaymentResult> RefundAsync(
        string transactionId,
        decimal? amount = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(PaymentResult.Failed(
            "Возвраты через RollyPay API не подключены — выполните возврат в ЛК RollyPay."));
    }

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    /// <summary>
    /// RollyPay statuses (docs: payment.created/paid/canceled/expired/
    /// chargeback/refunded). Unknown values return null so the caller can log
    /// and reject rather than silently treat them as final.
    /// </summary>
    private static PaymentStatus? MapStatus(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "paid" => PaymentStatus.Completed,
        "created" or "pending" => PaymentStatus.Pending,
        "canceled" or "cancelled" => PaymentStatus.Cancelled,
        "expired" => PaymentStatus.Expired,
        "refunded" or "chargeback" => PaymentStatus.Refunded,
        "failed" or "error" => PaymentStatus.Failed,
        _ => null,
    };

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

    /// <summary>Header lookup that tolerates casing differences across proxies.</summary>
    private static string? ReadHeader(Dictionary<string, string> headers, string name)
    {
        if (headers.TryGetValue(name, out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact;

        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(kv.Value))
            {
                return kv.Value;
            }
        }

        return null;
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
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var s) ? s : 0,
            _ => 0,
        };
    }
}
