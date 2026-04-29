using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.Enot;

/// <summary>
/// <see cref="IPaymentProvider"/> implementation for Enot.io.
///
/// Configuration is loaded from <see cref="PaymentProviderConfig"/> with
/// Name="enot":
///   ApiKey      → секретный ключ кассы (header <c>x-api-key</c>)
///   SecretKey   → дополнительный ключ (HMAC for webhook signature)
///   MerchantId  → shop_id (UUID)
///
/// API endpoints (https://docs.enot.io):
///   POST /invoice/create   — generate a hosted-checkout URL
///   GET  /invoice/info     — poll status
///   POST {hook_url}        — Enot calls our webhook on status change
/// </summary>
public class EnotPaymentProvider : IPaymentProvider
{
    private const string BaseUrl = "https://api.enot.io";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EnotPaymentProvider> _log;

    public EnotPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        ILogger<EnotPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _log = log;
    }

    public string Name => "enot";
    public string DisplayName => "Enot";

    public IReadOnlyList<PaymentMethod> SupportedMethods => new[]
    {
        PaymentMethod.Card,
        PaymentMethod.SBP,
        PaymentMethod.Crypto,
        PaymentMethod.Qiwi,
        PaymentMethod.YooMoney,
    };

    /// <summary>
    /// Reads <see cref="PaymentProviderConfig.IsEnabled"/> from DB once per call.
    /// Cheap (single row by indexed Name) and avoids stale-cache foot-guns when
    /// admin toggles the provider.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            var cfg = _db.PaymentProviderConfigs.AsNoTracking()
                .FirstOrDefault(c => c.Name == Name);
            return cfg?.IsEnabled == true;
        }
    }

    public bool SupportsRefund => false;

    // ────────────────────────────────────────────────────────────────────
    // Create payment
    // ────────────────────────────────────────────────────────────────────

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("Enot не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("Enot временно отключён.");

        // We use our own UUID as order_id — it goes out to Enot, comes back in
        // the webhook as `order_id`, and we look up the PaymentTransaction by it.
        var ourTransactionId = "kz-" + Guid.NewGuid().ToString("N");

        var body = new Dictionary<string, object?>
        {
            ["amount"]      = request.Amount,
            ["order_id"]    = ourTransactionId,
            ["currency"]    = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency,
            ["shop_id"]     = cfg.MerchantId,
            ["hook_url"]    = request.WebhookUrl,
            ["success_url"] = request.SuccessUrl,
            ["fail_url"]    = request.CancelUrl,
            ["comment"]     = request.Description,
            // Pass useful context back through the loop so the webhook can audit
            // the original purchase even if our DB row is somehow lost.
            ["custom_fields"] = JsonSerializer.Serialize(new
            {
                userId  = request.UserId,
                orderId = request.OrderId,
            }),
            ["expire"] = 300,
        };

        // Filter checkout methods by what the customer chose, when possible.
        if (request.Method == PaymentMethod.Card)
            body["include_service"] = new[] { "card" };
        else if (request.Method == PaymentMethod.SBP)
            body["include_service"] = new[] { "sbp" };
        else if (request.Method == PaymentMethod.Crypto)
            body["include_service"] = new[] { "bitcoin", "usdt_trc20", "usdt_erc20", "ethereum", "litecoin" };

        // Strip nulls so Enot's validator stays happy.
        var payload = body.Where(kv => kv.Value != null)
                          .ToDictionary(kv => kv.Key, kv => kv.Value!);

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Add("x-api-key", cfg.ApiKey);

        var requestJson = JsonSerializer.Serialize(payload);
        _log.LogInformation("Enot → POST /invoice/create order_id={OrderId} amount={Amount} method={Method} payload={Payload}",
            ourTransactionId, request.Amount, request.Method, requestJson);

        try
        {
            using var resp = await http.PostAsJsonAsync($"{BaseUrl}/invoice/create", payload, ct);
            var responseText = await resp.Content.ReadAsStringAsync(ct);

            _log.LogInformation("Enot ← /invoice/create http={Code} body={Body}",
                (int)resp.StatusCode, responseText);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Enot CreatePayment failed ({Code}): {Body}", (int)resp.StatusCode, responseText);
                return PaymentResult.Failed(ExtractError(responseText) ?? "Enot отказал в создании платежа",
                    ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(responseText);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                _log.LogWarning("Enot CreatePayment unexpected response: {Body}", responseText);
                return PaymentResult.Failed("Не удалось разобрать ответ Enot");
            }

            var invoiceId = data.GetProperty("id").GetString() ?? "";
            var url       = data.GetProperty("url").GetString() ?? "";

            _log.LogInformation("Enot invoice created: invoice_id={InvoiceId} url={Url}", invoiceId, url);

            return PaymentResult.Successful(
                transactionId: ourTransactionId,
                redirectUrl: url,
                providerTransactionId: invoiceId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Enot CreatePayment threw");
            return PaymentResult.Failed("Сетевая ошибка при обращении к Enot");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Status check
    // ────────────────────────────────────────────────────────────────────

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null)
            return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Failed,
                                             Message = "Enot не настроен" };

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.Add("x-api-key", cfg.ApiKey);

        try
        {
            // We use our internal transaction id as Enot's order_id.
            var url = $"{BaseUrl}/invoice/info?shop_id={cfg.MerchantId}&order_id={Uri.EscapeDataString(transactionId)}";
            using var resp = await http.GetAsync(url, ct);
            var responseText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Enot GetStatus failed ({Code}) for {Txn}: {Body}",
                    (int)resp.StatusCode, transactionId, responseText);
                return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Failed,
                                                 Message = ExtractError(responseText) };
            }

            using var doc = JsonDocument.Parse(responseText);
            var data = doc.RootElement.GetProperty("data");
            var statusStr = data.TryGetProperty("status", out var s) ? s.GetString() : "created";
            var amount    = data.TryGetProperty("invoice_amount", out var a) ? a.GetDecimal() : 0m;
            var currency  = data.TryGetProperty("currency", out var c) ? (c.GetString() ?? "RUB") : "RUB";

            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = MapStatus(statusStr),
                Amount        = amount,
                Currency      = currency,
                UpdatedAt     = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Enot GetStatus threw");
            return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Failed,
                                             Message = "Сетевая ошибка" };
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Webhook
    // ────────────────────────────────────────────────────────────────────

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return WebhookValidationResult.Invalid("Enot не настроен");
        if (string.IsNullOrEmpty(cfg.SecretKey))
            return WebhookValidationResult.Invalid("Enot SecretKey (Дополнительный ключ) не задан");

        // Header lookup is case-insensitive — Enot uses lowercase hyphens.
        var sigHeader = headers.FirstOrDefault(h =>
            string.Equals(h.Key, "x-api-sha256-signature", StringComparison.OrdinalIgnoreCase)).Value;

        if (string.IsNullOrWhiteSpace(sigHeader))
            return WebhookValidationResult.Invalid("Отсутствует заголовок x-api-sha256-signature");

        if (!EnotSignatureHelper.Verify(body, sigHeader, cfg.SecretKey))
        {
            // Helpful diagnostics — expected vs received, and the canonical
            // string we hashed. Sanitise to first 16 chars to keep logs short.
            try
            {
                var canonical = EnotSignatureHelper.CanonicalJson(body);
                var expected  = EnotSignatureHelper.Compute(body, cfg.SecretKey);
                _log.LogWarning(
                    "Enot webhook signature mismatch. Header: {Got}…, Expected: {Exp}…, Canonical: {Canon}",
                    sigHeader[..Math.Min(16, sigHeader.Length)],
                    expected[..16],
                    canonical.Length > 200 ? canonical[..200] + "…" : canonical);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not log signature diagnostics"); }

            return WebhookValidationResult.Invalid("Неверная подпись webhook");
        }

        // Signature OK — parse the payload.
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var orderId   = root.TryGetProperty("order_id", out var o) ? o.GetString() ?? "" : "";
            var statusStr = root.TryGetProperty("status",   out var s) ? s.GetString() ?? "" : "";
            var amount    = ParseAmount(root);

            return new WebhookValidationResult
            {
                IsValid        = true,
                TransactionId  = orderId,                 // ← matches PaymentTransaction.TransactionId
                NewStatus      = MapStatus(statusStr),
                Amount         = amount,
                RawData        = body,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Enot webhook body parse failed: {Body}", body);
            return WebhookValidationResult.Invalid("Невалидный JSON");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Refunds — not supported via API for now
    // ────────────────────────────────────────────────────────────────────

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default)
        => Task.FromResult(PaymentResult.Failed("Возвраты делаются вручную через личный кабинет Enot"));

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    private static PaymentStatus MapStatus(string? raw) => raw?.ToLowerInvariant() switch
    {
        "success" => PaymentStatus.Completed,
        "fail"    => PaymentStatus.Failed,
        "expired" => PaymentStatus.Expired,
        "refund"  => PaymentStatus.Refunded,
        "created" => PaymentStatus.Pending,
        _         => PaymentStatus.Pending,
    };

    private static decimal ParseAmount(JsonElement root)
    {
        if (!root.TryGetProperty("amount", out var a)) return 0m;
        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDecimal(),
            JsonValueKind.String => decimal.TryParse(a.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m,
            _ => 0m,
        };
    }

    private static string? ExtractError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                return err.ValueKind switch
                {
                    JsonValueKind.String => err.GetString(),
                    JsonValueKind.Array  => string.Join("; ", err.EnumerateArray().Select(e => e.ToString())),
                    JsonValueKind.Object => err.ToString(),
                    _ => null,
                };
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
