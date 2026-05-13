using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.Platega;

/// <summary>
/// <see cref="IPaymentProvider"/> for Platega (app.platega.io).
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="platega":
///   MerchantId  → X-MerchantId header (UUID issued in Platega LK)
///   ApiKey      → X-Secret header (the «API ключ» on the same page)
///   SecretKey   → unused; Platega has no body signature, the shared-secret
///                 header is the only auth and webhook-verification path
///   Settings    → optional JSON, e.g. { "baseUrl":"https://app.platega.io" }
///
/// API endpoints used:
///   POST /transaction/process    - create payment with a fixed method
///                                  (we pass 2 for СБП, 11 for карта)
///   GET  /transaction/{id}       - poll status (also used as a fallback
///                                  when the webhook is delayed)
///
/// Webhook flow: Platega POSTs JSON {id, amount, currency, status,
/// paymentMethod, payload} to our /api/webhooks/platega WITH the same
/// X-MerchantId / X-Secret headers we use outbound. Validation is a
/// straight string compare against our cfg — there's no HMAC. Status
/// enum: CONFIRMED (success) / CANCELED (failure) / CHARGEBACKED
/// (post-success refund / dispute). Platega expects 200 within 60 s
/// or it retries 3× at 5-min intervals.
/// </summary>
public class PlategaPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl = "https://app.platega.io";
    private const string HttpClientName = "platega";

    // Platega's paymentMethod enum (see «PaymentMethodInt» in their OpenAPI):
    //   2  → СБП (QR-код)
    //   3  → ЕРИП
    //   11 → Карточный эквайринг
    //   12 → Международная оплата
    //   13 → Криптовалюта
    private const int MethodSbp  = 2;
    private const int MethodCard = 11;

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<PlategaPaymentProvider> _log;

    public PlategaPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer, ILogger<PlategaPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "platega";
    public string DisplayName => "Platega";

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
            return cfg?.IsEnabled == true
                && !string.IsNullOrEmpty(cfg.MerchantId)   // X-MerchantId
                && !string.IsNullOrEmpty(cfg.ApiKey);      // X-Secret
        }
    }

    public bool SupportsRefund => false; // Platega refund API isn't published.

    // ════════════════════════════════════════════════════════════════════
    // Create payment
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("Platega не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("Platega временно отключён.");
        if (string.IsNullOrEmpty(cfg.MerchantId))
            return PaymentResult.Failed("Platega: не задан X-MerchantId (MerchantId).");
        if (string.IsNullOrEmpty(cfg.ApiKey))
            return PaymentResult.Failed("Platega: не задан X-Secret (ApiKey).");

        // Our internal txn-id — comes back in webhook via `payload`.
        // Platega assigns its own UUID `id`; we keep both linked.
        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);

        var methodId = request.Method switch
        {
            PaymentMethod.SBP  => MethodSbp,
            PaymentMethod.Card => MethodCard,
            _ => MethodCard,
        };

        // Anonymise email for whitelisted internal-test accounts so Platega's
        // antifraud doesn't cluster them. Real customers pass through unchanged.
        var contacts = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);

        // description: keep neutral («Платёж №…») — Platega's docs reserve
        // TgId:/UserId: prefixes for Stars sales, which we don't do. Leaking
        // brand tokens here just helps PSP antifraud cluster our shop.
        // Echo our internal txn-id in `payload` so the webhook can find the
        // PaymentTransaction without an extra lookup table.
        var bodyObj = new Dictionary<string, object?>
        {
            ["paymentMethod"]  = methodId,
            ["paymentDetails"] = new Dictionary<string, object?>
            {
                ["amount"]   = request.Amount,
                ["currency"] = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency,
            },
            ["description"] = request.Description ?? $"Платёж №{ourTransactionId}",
            ["return"]      = request.SuccessUrl,
            ["failedUrl"]   = request.CancelUrl,
            ["payload"]     = ourTransactionId,
        };

        var url = ReadBaseUrl(cfg) + "/transaction/process";
        var json = JsonSerializer.Serialize(bodyObj);

        try
        {
            _log.LogInformation(
                "Platega → POST {Url} txn={Txn} amount={Amt} method={M} body={Body}",
                url, ourTransactionId, request.Amount, methodId, json);

            using var http = CreateHttpClient(cfg);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            _log.LogInformation("Platega ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
            {
                return PaymentResult.Failed(
                    $"Platega вернул HTTP {(int)resp.StatusCode}: {raw}",
                    ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var transactionId = root.TryGetProperty("transactionId", out var t)
                ? t.GetString() : null;
            // The docs show two variants: `redirect` (with-method endpoint)
            // and `url` (no-method endpoint). Accept either so a future
            // switch to /v2/transaction/process doesn't break us.
            var redirectUrl = root.TryGetProperty("redirect", out var r) ? r.GetString() : null;
            if (string.IsNullOrEmpty(redirectUrl) && root.TryGetProperty("url", out var u))
                redirectUrl = u.GetString();

            if (string.IsNullOrEmpty(transactionId) || string.IsNullOrEmpty(redirectUrl))
            {
                return PaymentResult.Failed(
                    $"Platega не вернул transactionId / redirect URL: {raw}");
            }

            _log.LogInformation(
                "Platega → transaction created txn={Txn} platega_id={Pid} url={Url}",
                ourTransactionId, transactionId, redirectUrl);

            return new PaymentResult
            {
                Success = true,
                TransactionId = ourTransactionId,
                ProviderTransactionId = transactionId,
                RedirectUrl = redirectUrl,
                Status = PaymentStatus.Pending,
                ProviderData = new Dictionary<string, string>
                {
                    ["platega_transaction_id"] = transactionId,
                    ["redirect_url"] = redirectUrl,
                },
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Platega /transaction/process failed");
            return PaymentResult.Failed($"Platega недоступен: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Webhook validation
    // ════════════════════════════════════════════════════════════════════

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrEmpty(cfg.MerchantId) || string.IsNullOrEmpty(cfg.ApiKey))
            return WebhookValidationResult.Invalid("Platega not configured.");

        // Header lookup is case-insensitive per HTTP spec, but Dictionary
        // isn't by default. Build a case-insensitive view so we don't miss
        // the variant Platega sends today.
        var hdr = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

        if (!hdr.TryGetValue("X-MerchantId", out var hMerchant) ||
            !string.Equals(hMerchant, cfg.MerchantId, StringComparison.Ordinal))
        {
            _log.LogWarning("Platega webhook: X-MerchantId mismatch (got '{Got}')", hMerchant);
            return WebhookValidationResult.Invalid("X-MerchantId mismatch.");
        }
        if (!hdr.TryGetValue("X-Secret", out var hSecret) ||
            !string.Equals(hSecret, cfg.ApiKey, StringComparison.Ordinal))
        {
            _log.LogWarning("Platega webhook: X-Secret mismatch");
            return WebhookValidationResult.Invalid("X-Secret mismatch.");
        }

        // Body is JSON: {id, amount, currency, status, paymentMethod, payload}
        if (string.IsNullOrWhiteSpace(body))
            return WebhookValidationResult.Invalid("Empty body.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            return WebhookValidationResult.Invalid($"Body not JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            // Prefer our payload (= ourTransactionId we sent on create) — that
            // matches what we stored in PaymentTransaction.TransactionId. Fall
            // back to Platega's id if payload is missing.
            var payload = root.TryGetProperty("payload", out var pEl) ? pEl.GetString() : null;
            var txn = !string.IsNullOrEmpty(payload) ? payload : id;
            if (string.IsNullOrEmpty(txn))
                return WebhookValidationResult.Invalid("Missing id/payload.");

            var statusRaw = root.TryGetProperty("status", out var sEl) ? sEl.GetString() : null;
            var newStatus = (statusRaw ?? "").ToUpperInvariant() switch
            {
                "CONFIRMED"    => PaymentStatus.Completed,
                "CANCELED"     => PaymentStatus.Failed,
                "CHARGEBACKED" => PaymentStatus.Refunded,
                _              => PaymentStatus.Pending,
            };

            decimal amount = 0;
            if (root.TryGetProperty("amount", out var aEl))
            {
                if (aEl.ValueKind == JsonValueKind.Number) aEl.TryGetDecimal(out amount);
                else if (aEl.ValueKind == JsonValueKind.String &&
                         decimal.TryParse(aEl.GetString(), System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var p))
                    amount = p;
            }

            return new WebhookValidationResult
            {
                IsValid       = true,
                TransactionId = txn,
                NewStatus     = newStatus,
                Amount        = amount,
                RawData       = body,
                ResponseBody  = null, // any 2xx body satisfies their 200-OK rule
            };
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Status polling
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId,
        CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrEmpty(cfg.MerchantId) || string.IsNullOrEmpty(cfg.ApiKey))
        {
            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = PaymentStatus.Failed,
                Message       = "Platega not configured.",
            };
        }

        // Platega's GET /transaction/{id} wants their UUID, not our internal
        // TxnId. Look up the provider id from the transactions table if the
        // caller passed our internal one.
        var providerTxn = await _db.PaymentTransactions.AsNoTracking()
            .Where(t => t.ProviderName == Name &&
                       (t.TransactionId == transactionId || t.ProviderTransactionId == transactionId))
            .Select(t => t.ProviderTransactionId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(providerTxn))
            providerTxn = transactionId;

        var url = $"{ReadBaseUrl(cfg)}/transaction/{Uri.EscapeDataString(providerTxn!)}";
        try
        {
            using var http = CreateHttpClient(cfg);
            using var resp = await http.GetAsync(url, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new PaymentStatusResult
                {
                    TransactionId = providerTxn,
                    Status        = PaymentStatus.Failed,
                    Message       = $"HTTP {(int)resp.StatusCode}: {raw}",
                };
            }
            using var doc = JsonDocument.Parse(raw);
            var st = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            return new PaymentStatusResult
            {
                TransactionId = providerTxn,
                Status = (st ?? "").ToUpperInvariant() switch
                {
                    "CONFIRMED"    => PaymentStatus.Completed,
                    "CANCELED"     => PaymentStatus.Failed,
                    "CHARGEBACKED" => PaymentStatus.Refunded,
                    "PENDING"      => PaymentStatus.Pending,
                    _              => PaymentStatus.Pending,
                },
                UpdatedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            return new PaymentStatusResult
            {
                TransactionId = providerTxn ?? transactionId,
                Status        = PaymentStatus.Failed,
                Message       = ex.Message,
            };
        }
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(PaymentResult.Failed(
            "Возврат через Platega API не задокументирован — обратитесь в их саппорт."));
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

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

    private HttpClient CreateHttpClient(PaymentProviderConfig cfg)
    {
        var http = _httpFactory.CreateClient(HttpClientName);
        // Wipe any default headers from a previous use (named client is
        // reused across requests via IHttpClientFactory pooling) so we
        // never accidentally ship a stale X-Secret from a rotated key.
        http.DefaultRequestHeaders.Remove("X-MerchantId");
        http.DefaultRequestHeaders.Remove("X-Secret");
        http.DefaultRequestHeaders.Add("X-MerchantId", cfg.MerchantId);
        http.DefaultRequestHeaders.Add("X-Secret",     cfg.ApiKey);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }
}
