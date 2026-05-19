using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.BlvckPay;

/// <summary>
/// <see cref="IPaymentProvider"/> for BlvckPay (payment.blvckpay.com, OpenAPI
/// v0.1.0). A modern JSON REST gateway covering СБП, bank cards (Visa/MC and
/// МИР) and Steam top-ups. We wire СБП and МИР-cards here; Steam is a separate
/// flow used by the Steam top-up feature.
///
/// Configuration in <see cref="PaymentProviderConfig"/> (Name="blvckpay"):
///   ApiKey      → signature token for СБП (issued by BlvckPay at connection)
///   SecretKey   → signature token for cards; falls back to ApiKey if empty
///   Settings    → optional JSON {"baseUrl":"https://payment.blvckpay.com/api/v1"}
///
/// Flows:
///   • СБП  — POST /sbp/order/create → {message:{url, order_id}} → redirect.
///   • Card — POST /acquiring/card/create (currency=RUB for МИР) → same shape.
///   Status: POST /sbp/order/check or /acquiring/card/info → {message:{status}}
///   where status ∈ Pending / Expired / Paid.
///
/// signature: per their docs «уникальная подпись, которую мы выдаём при
/// подключении» — treated here as a static token placed in every request.
/// If BlvckPay returns 403 we'll switch to a computed signature once they
/// publish the formula.
///
/// Webhook: BlvckPay POSTs the same JSON as the matching */check endpoint.
/// We correlate by the BlvckPay order_id (stored as ProviderTransactionId).
/// </summary>
public class BlvckPayPaymentProvider : IPaymentProvider
{
    // Prod API serves endpoints at the root (/sbp/order/create, …). The
    // OpenAPI «servers: /api/v1» prefix only applies to the test host
    // (test.blvckpay.com/api/v1). Override per-env via Settings.baseUrl.
    private const string DefaultBaseUrl = "https://payment.blvckpay.com";
    private const string HttpClientName = "blvckpay";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<BlvckPayPaymentProvider> _log;

    public BlvckPayPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer, ILogger<BlvckPayPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "blvckpay";
    public string DisplayName => "BlvckPay";

    public IReadOnlyList<PaymentMethod> SupportedMethods => new[]
    {
        PaymentMethod.Card,
        PaymentMethod.SBP,
    };

    public bool IsEnabled
    {
        get
        {
            var cfg = _db.PaymentProviderConfigs.AsNoTracking()
                .FirstOrDefault(c => c.Name == Name);
            return cfg?.IsEnabled == true && !string.IsNullOrEmpty(cfg.ApiKey);
        }
    }

    public bool SupportsRefund => false;

    // ════════════════════════════════════════════════════════════════════
    // Create payment
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("BlvckPay не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("BlvckPay временно отключён.");
        if (string.IsNullOrEmpty(cfg.ApiKey))
            return PaymentResult.Failed("BlvckPay: не задан токен подписи (ApiKey).");

        var isCard = request.Method == PaymentMethod.Card;
        var signature = isCard
            ? (string.IsNullOrEmpty(cfg.SecretKey) ? cfg.ApiKey! : cfg.SecretKey!)
            : cfg.ApiKey!;

        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);
        var amount      = decimal.Round(request.Amount, 2);
        var description = request.Description ?? $"Заказ {ourTransactionId}";
        var contacts    = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);
        var backLink    = request.SuccessUrl ?? "";
        var errorLink   = request.CancelUrl  ?? "";

        // payload is echoed back to us in /check and the webhook — we stash our
        // own transaction id so correlation never depends on order_id alone.
        var payload = new Dictionary<string, string> { ["txid"] = ourTransactionId };

        object body = isCard
            ? new
            {
                amount,
                description,
                signature,
                currency  = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency,
                payload,
                email     = contacts.Email,
                phone     = contacts.Phone,
                backLink,
                errorLink,
            }
            : new
            {
                amount,
                signature,
                description,
                backLink,
                payload,
                email = contacts.Email,
                phone = contacts.Phone,
            };

        var path = isCard ? "/acquiring/card/create" : "/sbp/order/create";
        var url  = ReadBaseUrl(cfg) + path;

        try
        {
            _log.LogInformation(
                "BlvckPay → POST {Url} txn={Txn} amount={Amt} method={Method}",
                url, ourTransactionId, amount, request.Method);

            using var http = _httpFactory.CreateClient(HttpClientName);
            using var content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            _log.LogInformation("BlvckPay ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
            {
                var hint = (int)resp.StatusCode switch
                {
                    403 => "BlvckPay: ошибка подписи (403). Возможно, signature нужно вычислять, а не статичный токен.",
                    409 => "BlvckPay: сумма меньше минимальной (409).",
                    422 => "BlvckPay: неверный формат данных (422).",
                    _   => $"BlvckPay вернул HTTP {(int)resp.StatusCode}.",
                };
                return PaymentResult.Failed($"{hint} {raw}", ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Actual responses are flat — url/order_id/status sit at the root,
            // not under a "message" wrapper (the OpenAPI examples are
            // idealised). Be defensive: use "message" if present, else root.
            var data = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
                ? m : root;

            // Surface explicit error envelopes (e.g. {"detail":"Merchant will not find"}).
            if (root.TryGetProperty("detail", out var det))
                return PaymentResult.Failed($"BlvckPay: {det}", "detail");

            var payUrl  = data.TryGetProperty("url", out var u) ? u.GetString() : null;
            var orderId = ReadOrderId(data);

            if (string.IsNullOrEmpty(payUrl))
                return PaymentResult.Failed($"BlvckPay: пустой url для редиректа. {raw}");
            if (string.IsNullOrEmpty(orderId))
                return PaymentResult.Failed($"BlvckPay: пустой order_id. {raw}");

            _log.LogInformation(
                "BlvckPay → order created txn={Txn} order_id={Order} url={Url}",
                ourTransactionId, orderId, payUrl);

            return new PaymentResult
            {
                Success               = true,
                TransactionId         = ourTransactionId,
                ProviderTransactionId = orderId,
                RedirectUrl           = payUrl,
                Status                = PaymentStatus.Pending,
                ProviderData          = new Dictionary<string, string>
                {
                    ["order_id"] = orderId,
                    ["pay_url"]  = payUrl,
                },
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "BlvckPay create failed");
            return PaymentResult.Failed($"BlvckPay недоступен: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Status check
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        var tx = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);

        if (cfg == null || tx == null || string.IsNullOrEmpty(tx.ProviderTransactionId))
            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = tx?.Status ?? PaymentStatus.Pending,
                Message       = "Нет данных для проверки статуса.",
            };

        var isCard = tx.Method == PaymentMethod.Card;
        var signature = isCard
            ? (string.IsNullOrEmpty(cfg.SecretKey) ? cfg.ApiKey! : cfg.SecretKey!)
            : cfg.ApiKey!;
        var path = isCard ? "/acquiring/card/info" : "/sbp/order/check";
        var url  = ReadBaseUrl(cfg) + path;

        try
        {
            using var http = _httpFactory.CreateClient(HttpClientName);
            var body = JsonSerializer.Serialize(new { signature, order_id = tx.ProviderTransactionId });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new PaymentStatusResult
                {
                    TransactionId = transactionId,
                    Status        = tx.Status,
                    Message       = $"BlvckPay status HTTP {(int)resp.StatusCode}",
                };

            using var doc = JsonDocument.Parse(raw);
            var msg = doc.RootElement.TryGetProperty("message", out var m) ? m : doc.RootElement;
            var statusStr = msg.TryGetProperty("status", out var s) ? s.GetString() : null;
            var amount = ReadAmount(msg);

            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = MapStatus(statusStr),
                Amount        = amount > 0 ? amount : tx.Amount,
                Currency      = tx.Currency,
                UpdatedAt     = DateTime.UtcNow,
                Message       = statusStr,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "BlvckPay status check failed for {Txn}", transactionId);
            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = tx.Status,
                Message       = ex.Message,
            };
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Webhook
    // ════════════════════════════════════════════════════════════════════

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            return WebhookValidationResult.Invalid("Пустое тело webhook.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch { return WebhookValidationResult.Invalid("BlvckPay webhook: тело не JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            // Webhook mirrors the */check response — fields live under
            // "message" there, but be defensive and accept them at root too.
            var msg = root.TryGetProperty("message", out var m) ? m : root;

            var orderId = ReadOrderId(msg);
            var statusStr = msg.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (string.IsNullOrEmpty(orderId))
                return WebhookValidationResult.Invalid("BlvckPay webhook: нет order_id.");

            // Correlate to our transaction. Primary: payload.txid (we set it on
            // create); fallback: match BlvckPay order_id ↔ ProviderTransactionId.
            string? ourTxnId = null;
            if (msg.TryGetProperty("payload", out var pl) && pl.ValueKind == JsonValueKind.Object
                && pl.TryGetProperty("txid", out var tid))
                ourTxnId = tid.GetString();

            var tx = !string.IsNullOrEmpty(ourTxnId)
                ? await _db.PaymentTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TransactionId == ourTxnId, ct)
                : await _db.PaymentTransactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.ProviderName == Name
                                           && t.ProviderTransactionId == orderId, ct);

            if (tx == null)
            {
                _log.LogWarning("BlvckPay webhook: транзакция не найдена (order_id={Order} txid={Txid})",
                    orderId, ourTxnId);
                return WebhookValidationResult.Invalid("BlvckPay webhook: транзакция не найдена.");
            }

            var amount = ReadAmount(msg);

            _log.LogInformation(
                "BlvckPay webhook: txn={Txn} order_id={Order} status={Status}",
                tx.TransactionId, orderId, statusStr);

            return new WebhookValidationResult
            {
                IsValid       = true,
                TransactionId = tx.TransactionId,
                NewStatus     = MapStatus(statusStr),
                Amount        = amount > 0 ? amount : tx.Amount,
                RawData       = body,
                ResponseBody  = "ok",
            };
        }
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default)
        => Task.FromResult(PaymentResult.Failed("Возвраты BlvckPay делаются вручную через их ЛК."));

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    private static PaymentStatus MapStatus(string? status) => (status ?? "").ToLowerInvariant() switch
    {
        "paid"      => PaymentStatus.Completed,
        "expired"   => PaymentStatus.Expired,
        "pending"   => PaymentStatus.Pending,
        "cancelled" => PaymentStatus.Cancelled,
        "canceled"  => PaymentStatus.Cancelled,
        _           => PaymentStatus.Pending,
    };

    private static string? ReadOrderId(JsonElement el)
    {
        if (!el.TryGetProperty("order_id", out var o)) return null;
        return o.ValueKind switch
        {
            JsonValueKind.Number => o.GetInt64().ToString(),
            JsonValueKind.String => o.GetString(),
            _ => null,
        };
    }

    private static decimal ReadAmount(JsonElement el)
    {
        if (!el.TryGetProperty("amount", out var a)) return 0m;
        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(a.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m,
            _ => 0m,
        };
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
                    var v = b.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.TrimEnd('/');
                }
            }
            catch { /* malformed Settings — fall back to default */ }
        }
        return DefaultBaseUrl;
    }
}
