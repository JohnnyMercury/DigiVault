using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.ParityPay;

/// <summary>
/// <see cref="IPaymentProvider"/> for ParityPay (api.paritypay.ru), exposed to
/// customers as ParityPay in the step-2 payment picker.
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="paritypay":
///   MerchantId  -> shop_id (UUID from the ParityPay cashier settings)
///   ApiKey      -> secret key #1 for outbound API request signatures
///   SecretKey   -> secret key #2 for payment webhook signatures
///   Settings    -> optional JSON {"baseUrl":"https://api.paritypay.ru","expireMinutes":60}
///
/// API endpoints used:
///   POST /invoice/create - create incoming SBP invoice, returns id + link
///   POST /invoice/status - poll invoice status by id or order_id
///
/// Signature: sort flat JSON parameters by key, concatenate values, then
/// HMAC-SHA256 with key #1 for API requests and key #2 for webhooks.
/// </summary>
public class ParityPayPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl = "https://api.paritypay.ru";
    private const string HttpClientName = "paritypay";
    private const int DefaultExpireMinutes = 60;
    private const int MaxExpireMinutes = 43200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<ParityPayPaymentProvider> _log;

    public ParityPayPaymentProvider(
        ApplicationDbContext db,
        IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer,
        ILogger<ParityPayPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "paritypay";
    public string DisplayName => "ParityPay";

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
            return cfg?.IsEnabled == true
                && !string.IsNullOrWhiteSpace(cfg.MerchantId)
                && !string.IsNullOrWhiteSpace(cfg.ApiKey)
                && !string.IsNullOrWhiteSpace(cfg.SecretKey);
        }
    }

    public bool SupportsRefund => false;

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("ParityPay не настроена в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("ParityPay временно отключена.");
        if (string.IsNullOrWhiteSpace(cfg.MerchantId))
            return PaymentResult.Failed("ParityPay: не задан shop_id (MerchantId).");
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            return PaymentResult.Failed("ParityPay: не задан секретный ключ №1 (ApiKey).");
        if (string.IsNullOrWhiteSpace(cfg.SecretKey))
            return PaymentResult.Failed("ParityPay: не задан секретный ключ №2 (SecretKey).");

        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);
        var amount = RoundAmountForJson(request.Amount);
        var expireMinutes = ReadExpireMinutes(cfg);

        // Randomise the outbound user id for whitelisted internal-test accounts
        // so ParityPay's antifraud can't cluster their repeat purchases on a
        // constant user_hash. Real customers keep their actual id. Crediting
        // keys on our stored PaymentTransaction.UserId, so this value is
        // cosmetic to us. Same value in custom_fields to stay consistent.
        var userHash = _anonymizer.AnonymizeUserId(request.Email, request.UserId);

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["shop_id"] = cfg.MerchantId!,
            ["amount"] = amount,
            ["order_id"] = ourTransactionId,
            ["service"] = "sbp",
            ["expire"] = expireMinutes,
            ["user_hash"] = userHash,
            ["custom_fields"] = request.OrderId.HasValue
                ? $"order:{request.OrderId.Value}"
                : $"userid:{userHash}",
            ["comment"] = request.Description ?? $"Order {ourTransactionId}",
        };

        if (!string.IsNullOrEmpty(request.SuccessUrl)) payload["success_url"] = request.SuccessUrl;
        if (!string.IsNullOrEmpty(request.CancelUrl)) payload["fail_url"] = request.CancelUrl;
        if (!string.IsNullOrEmpty(request.WebhookUrl)) payload["callback_url"] = request.WebhookUrl;

        var signature = ParityPaySignatureHelper.BuildSignature(payload, cfg.ApiKey!);
        var bodyJson = JsonSerializer.Serialize(payload, JsonOptions);
        var url = ReadBaseUrl(cfg) + "/invoice/create";

        try
        {
            _log.LogInformation(
                "ParityPay → POST {Url} txn={Txn} amount={Amount}",
                url, ourTransactionId, request.Amount);

            using var http = _httpFactory.CreateClient(HttpClientName);
            http.DefaultRequestHeaders.Remove("X-SIGNATURE");
            http.DefaultRequestHeaders.Add("X-SIGNATURE", signature);

            using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            _log.LogInformation("ParityPay ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
            {
                return PaymentResult.Failed(
                    $"ParityPay вернула HTTP {(int)resp.StatusCode}: {raw}",
                    ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
                return PaymentResult.Failed($"ParityPay отказала в создании счёта: {ReadElementAsString(error) ?? raw}");

            var invoiceId = ReadString(root, "id");
            var status = ReadString(root, "status");
            var link = ReadString(root, "link");

            if (string.IsNullOrEmpty(invoiceId))
                return PaymentResult.Failed($"ParityPay не вернула id инвойса: {raw}");
            if (string.IsNullOrEmpty(link))
                return PaymentResult.Failed($"ParityPay не вернула ссылку оплаты: {raw}");

            return new PaymentResult
            {
                Success = true,
                TransactionId = ourTransactionId,
                ProviderTransactionId = invoiceId,
                RedirectUrl = link,
                Status = MapStatus(status),
                ProviderData = new Dictionary<string, string>
                {
                    ["invoice_id"] = invoiceId,
                    ["pay_url"] = link,
                    ["service"] = "sbp",
                },
                SentContacts = new SentContactData(
                    Email: null, Phone: null, Name: null,
                    UserId: userHash, Ip: null,
                    Anonymized: _anonymizer.ShouldAnonymize(request.Email)),
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ParityPay /invoice/create failed");
            return PaymentResult.Failed($"ParityPay недоступна: {ex.Message}");
        }
    }

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(
        string transactionId,
        CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.MerchantId) || string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status = PaymentStatus.Failed,
                Message = "ParityPay not configured.",
            };
        }

        var tx = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ProviderName == Name &&
                (t.TransactionId == transactionId || t.ProviderTransactionId == transactionId), ct);

        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["shop_id"] = cfg.MerchantId!,
        };

        if (!string.IsNullOrEmpty(tx?.ProviderTransactionId))
            payload["id"] = tx.ProviderTransactionId;
        else
            payload["order_id"] = transactionId;

        var signature = ParityPaySignatureHelper.BuildSignature(payload, cfg.ApiKey!);
        var bodyJson = JsonSerializer.Serialize(payload, JsonOptions);
        var url = ReadBaseUrl(cfg) + "/invoice/status";

        try
        {
            using var http = _httpFactory.CreateClient(HttpClientName);
            http.DefaultRequestHeaders.Remove("X-SIGNATURE");
            http.DefaultRequestHeaders.Add("X-SIGNATURE", signature);

            using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                return new PaymentStatusResult
                {
                    TransactionId = transactionId,
                    Status = tx?.Status ?? PaymentStatus.Failed,
                    Message = $"HTTP {(int)resp.StatusCode}: {raw}",
                };
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                return new PaymentStatusResult
                {
                    TransactionId = transactionId,
                    Status = tx?.Status ?? PaymentStatus.Pending,
                    Message = ReadElementAsString(error),
                };
            }

            var amount = ReadDecimal(root, "amount");
            var status = ReadString(root, "status");

            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status = MapStatus(status),
                Amount = amount > 0 ? amount : tx?.Amount ?? 0,
                Currency = tx?.Currency ?? "RUB",
                UpdatedAt = DateTime.UtcNow,
                Message = status,
                ProviderData = new Dictionary<string, string>
                {
                    ["invoice_id"] = ReadString(root, "id") ?? tx?.ProviderTransactionId ?? "",
                    ["order_id"] = ReadString(root, "order_id") ?? tx?.TransactionId ?? transactionId,
                },
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ParityPay status check failed for {Txn}", transactionId);
            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status = tx?.Status ?? PaymentStatus.Failed,
                Message = ex.Message,
            };
        }
    }

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers,
        string body,
        CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrWhiteSpace(cfg.MerchantId) || string.IsNullOrWhiteSpace(cfg.SecretKey))
            return WebhookValidationResult.Invalid("ParityPay not configured.");

        if (string.IsNullOrWhiteSpace(body))
            return WebhookValidationResult.Invalid("ParityPay webhook: empty body.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            return WebhookValidationResult.Invalid($"ParityPay webhook: body is not JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var hdr = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            if (!hdr.TryGetValue("X-SIGNATURE", out var actualSignature))
                return WebhookValidationResult.Invalid("ParityPay webhook: X-SIGNATURE missing.");

            var expectedSignature = ParityPaySignatureHelper.BuildSignature(root, cfg.SecretKey!);
            if (!ParityPaySignatureHelper.FixedTimeEquals(actualSignature, expectedSignature))
            {
                _log.LogWarning("ParityPay webhook signature mismatch. Got {Got}, expected {Expected}",
                    actualSignature, expectedSignature);
                return WebhookValidationResult.Invalid("ParityPay webhook: signature mismatch.");
            }

            var shopId = ReadString(root, "shop_id");
            if (!string.Equals(shopId, cfg.MerchantId, StringComparison.Ordinal))
                return WebhookValidationResult.Invalid("ParityPay webhook: shop_id mismatch.");

            var orderId = ReadString(root, "order_id");
            var invoiceId = ReadString(root, "id");
            var txnId = !string.IsNullOrEmpty(orderId) ? orderId : invoiceId;
            if (string.IsNullOrEmpty(txnId))
                return WebhookValidationResult.Invalid("ParityPay webhook: missing order_id/id.");

            var status = ReadString(root, "status");
            var amount = ReadDecimal(root, "amount");

            return new WebhookValidationResult
            {
                IsValid = true,
                TransactionId = txnId,
                NewStatus = MapStatus(status),
                Amount = amount,
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
            "Возвраты через ParityPay API не подключены — выполните возврат вручную в ЛК ParityPay."));
    }

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    private static PaymentStatus MapStatus(string? status) => (status ?? "").Trim().ToUpperInvariant() switch
    {
        "PAID" => PaymentStatus.Completed,
        "EXPIRED" => PaymentStatus.Expired,
        "REFUNDED" => PaymentStatus.Refunded,
        "FAILED" or "FAIL" or "ERROR" => PaymentStatus.Failed,
        "NEW" => PaymentStatus.Pending,
        _ => PaymentStatus.Pending,
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

    private static int ReadExpireMinutes(PaymentProviderConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Settings))
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg.Settings);
                if (doc.RootElement.TryGetProperty("expireMinutes", out var e)
                    && e.TryGetInt32(out var minutes)
                    && minutes > 0)
                {
                    return Math.Min(minutes, MaxExpireMinutes);
                }
            }
            catch { /* malformed Settings - fall back to default */ }
        }

        return DefaultExpireMinutes;
    }

    private static object RoundAmountForJson(decimal amount)
    {
        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (rounded == decimal.Truncate(rounded))
            return (long)rounded;

        return double.Parse(
            rounded.ToString("0.##", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return ReadElementAsString(value);
    }

    private static string? ReadElementAsString(JsonElement value)
    {
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
