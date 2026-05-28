using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.BillionPay;

/// <summary>
/// <see cref="IPaymentProvider"/> for BillionPay (api.billionpay.cc, v2.1.2).
///
/// Configuration (PaymentProviderConfig, Name="billionpay"):
///   ApiKey      → X-API-Key (public key)
///   SecretKey   → secret used for HMAC-SHA512 signing
///   MerchantId  → optional merchantUserID (currently unused — passed UserId)
///   Settings    → optional JSON {"baseUrl":"https://api.billionpay.cc","bank":"ANY_BANK"}
///
/// Flows used here:
///   • SBP  → method "SBP", bank ANY_BANK (the BillionPay form delivers QR / NSPK link)
///   • Card → method "ECOM_PAYMENT_LINK" (hosted card page; returns paymentFormUrl)
///
/// Signature: HMAC-SHA512(secret, message), hex, header X-API-Sign.
///   POST:  message = "{path}{minified-json-with-sorted-keys}"
///   GET:   message = "{path}{querystring-sorted-no-leading-?}"
///
/// Webhook: BillionPay POSTs only {trackerID,status,externalID,amount,commission}.
/// We use externalID (= our TransactionId) to find the transaction and the status
/// to mark Completed/Failed. The doc recommends a follow-up GET /v1/payIn for the
/// full record; for now the callback alone carries enough to close the txn.
/// </summary>
public class BillionPayPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl   = "https://api.billionpay.cc";
    private const string HttpClientName   = "billionpay";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<BillionPayPaymentProvider> _log;

    public BillionPayPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer, ILogger<BillionPayPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "billionpay";
    public string DisplayName => "BillionPay";

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
                && !string.IsNullOrEmpty(cfg.ApiKey)
                && !string.IsNullOrEmpty(cfg.SecretKey);
        }
    }

    public bool SupportsRefund => false;

    // ════════════════════════════════════════════════════════════════════
    // CreatePaymentAsync
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("BillionPay не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("BillionPay временно отключён.");
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey))
            return PaymentResult.Failed("BillionPay: не заданы API key / secret.");

        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);

        var isCard   = request.Method == PaymentMethod.Card;
        var method   = isCard ? "ECOM_PAYMENT_LINK" : "SBP";
        var bank     = ReadBank(cfg);  // "ANY_BANK" by default
        var amount   = decimal.Round(request.Amount, 2);
        var currency = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency;
        var contacts = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);

        // Build sorted-keys JSON for signing. Order matters for the signature:
        // BillionPay verifies by re-serialising our body with sorted keys.
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["amount"]         = amount,
            ["bank"]           = bank,
            ["callbackURL"]    = request.WebhookUrl ?? "",
            ["currency"]       = currency,
            ["externalID"]     = ourTransactionId,
            ["merchantUserID"] = request.UserId,
            ["method"]         = method,
        };
        if (!string.IsNullOrEmpty(request.SuccessUrl))
            payload["redirectURL"] = request.SuccessUrl;
        if (!string.IsNullOrEmpty(contacts.Phone))
            payload["phoneNumber"] = contacts.Phone;

        var bodyJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        const string path = "/v1/payIn";
        var signMessage = path + bodyJson;
        var signature = HmacSha512Hex(cfg.SecretKey!, signMessage);

        var baseUrl = ReadBaseUrl(cfg);
        try
        {
            _log.LogInformation(
                "BillionPay → POST {Url} txn={Txn} method={Method} amount={Amt}",
                baseUrl + path, ourTransactionId, method, amount);

            using var http = _httpFactory.CreateClient(HttpClientName);
            http.DefaultRequestHeaders.Remove("X-API-Key");
            http.DefaultRequestHeaders.Remove("X-API-Sign");
            http.DefaultRequestHeaders.Add("X-API-Key", cfg.ApiKey!);
            http.DefaultRequestHeaders.Add("X-API-Sign", signature);

            using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(baseUrl + path, content, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            _log.LogInformation("BillionPay ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
                return PaymentResult.Failed(
                    $"BillionPay HTTP {(int)resp.StatusCode}: {raw}",
                    ((int)resp.StatusCode).ToString());

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var ok) && !ok.GetBoolean())
            {
                var msg = root.TryGetProperty("error", out var err)
                          && err.TryGetProperty("message", out var em) ? em.GetString() : raw;
                return PaymentResult.Failed($"BillionPay: {msg}");
            }

            if (!root.TryGetProperty("data", out var data))
                return PaymentResult.Failed($"BillionPay: нет data в ответе. {raw}");

            var trackerId = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

            // Where the customer goes next, in priority order:
            //   1) paymentFormUrl   — ECOM card hosted page
            //   2) nspkURL          — SBP NSPK pay-page
            //   3) deeplink         — bank app deeplink
            //   4) EWalletPaymentLink — wallet
            string? payUrl = null;
            if (data.TryGetProperty("paymentFormUrl", out var pfu) && pfu.ValueKind == JsonValueKind.String)
                payUrl = pfu.GetString();
            if (string.IsNullOrEmpty(payUrl) && data.TryGetProperty("nspkURL", out var nu) && nu.ValueKind == JsonValueKind.String)
                payUrl = nu.GetString();
            if (string.IsNullOrEmpty(payUrl) && data.TryGetProperty("deeplink", out var dl) && dl.ValueKind == JsonValueKind.String)
                payUrl = dl.GetString();
            if (string.IsNullOrEmpty(payUrl) && data.TryGetProperty("EWalletPaymentLink", out var wl) && wl.ValueKind == JsonValueKind.String)
                payUrl = wl.GetString();

            if (string.IsNullOrEmpty(payUrl))
                return PaymentResult.Failed($"BillionPay: не вернул ссылку оплаты. {raw}");

            _log.LogInformation(
                "BillionPay → invoice created txn={Txn} tracker={Tracker} url={Url}",
                ourTransactionId, trackerId, payUrl);

            return new PaymentResult
            {
                Success               = true,
                TransactionId         = ourTransactionId,
                ProviderTransactionId = trackerId,
                RedirectUrl           = payUrl,
                Status                = PaymentStatus.Pending,
                ProviderData          = new Dictionary<string, string>
                {
                    ["tracker_id"] = trackerId ?? "",
                    ["pay_url"]    = payUrl,
                    ["method"]     = method,
                },
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "BillionPay create failed");
            return PaymentResult.Failed($"BillionPay недоступен: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Webhook validation
    //
    // BillionPay callback body (CallbackPayload):
    //   { trackerID, status: SUCCESS|ERROR, externalID, amount?, commission? }
    // No callback signature is documented; we trust the body if externalID
    // matches one of our transactions (defensive) and respond 200.
    // ════════════════════════════════════════════════════════════════════

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            return WebhookValidationResult.Invalid("Empty body");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch { return WebhookValidationResult.Invalid("BillionPay webhook: не JSON"); }

        using (doc)
        {
            var root = doc.RootElement;
            var externalId = root.TryGetProperty("externalID", out var e) ? e.GetString() : null;
            var trackerId  = root.TryGetProperty("trackerID",  out var t) ? t.GetString() : null;
            var statusStr  = root.TryGetProperty("status",     out var s) ? s.GetString() : null;

            if (string.IsNullOrEmpty(externalId))
                return WebhookValidationResult.Invalid("BillionPay webhook: нет externalID");

            var tx = await _db.PaymentTransactions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TransactionId == externalId && x.ProviderName == Name, ct);
            if (tx == null)
            {
                _log.LogWarning("BillionPay webhook: txn {Txn} not found (tracker {Tracker})", externalId, trackerId);
                return WebhookValidationResult.Invalid("Transaction not found");
            }

            decimal amount = 0;
            if (root.TryGetProperty("amount", out var aEl))
            {
                if (aEl.ValueKind == JsonValueKind.String)
                    decimal.TryParse(aEl.GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out amount);
                else if (aEl.ValueKind == JsonValueKind.Number)
                    amount = aEl.GetDecimal();
            }

            var newStatus = (statusStr ?? "").ToUpperInvariant() switch
            {
                "SUCCESS" => PaymentStatus.Completed,
                "ERROR"   => PaymentStatus.Failed,
                _         => PaymentStatus.Pending,
            };

            _log.LogInformation(
                "BillionPay webhook: txn={Txn} tracker={Tracker} status={Status} → {New}",
                externalId, trackerId, statusStr, newStatus);

            return new WebhookValidationResult
            {
                IsValid       = true,
                TransactionId = externalId,
                NewStatus     = newStatus,
                Amount        = amount > 0 ? amount : tx.Amount,
                RawData       = body,
            };
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Status polling — GET /v1/payIn?id={trackerID}
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
                Message       = "No tracker id for status query.",
            };

        const string path = "/v1/payIn";
        var query   = "id=" + Uri.EscapeDataString(tx.ProviderTransactionId);
        var signMsg = path + query;
        var sig     = HmacSha512Hex(cfg.SecretKey!, signMsg);

        try
        {
            using var http = _httpFactory.CreateClient(HttpClientName);
            http.DefaultRequestHeaders.Remove("X-API-Key");
            http.DefaultRequestHeaders.Remove("X-API-Sign");
            http.DefaultRequestHeaders.Add("X-API-Key", cfg.ApiKey!);
            http.DefaultRequestHeaders.Add("X-API-Sign", sig);

            using var resp = await http.GetAsync(ReadBaseUrl(cfg) + path + "?" + query, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new PaymentStatusResult { TransactionId = transactionId, Status = tx.Status,
                                                  Message = $"BillionPay HTTP {(int)resp.StatusCode}" };

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return new PaymentStatusResult { TransactionId = transactionId, Status = tx.Status, Message = raw };

            var st = data.TryGetProperty("status", out var s) ? s.GetString() : null;
            var newStatus = (st ?? "").ToLowerInvariant() switch
            {
                "success" => PaymentStatus.Completed,
                "error"   => PaymentStatus.Failed,
                "pending" => PaymentStatus.Pending,
                "created" => PaymentStatus.Pending,
                _         => PaymentStatus.Pending,
            };

            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = newStatus,
                Amount        = tx.Amount,
                Currency      = tx.Currency,
                UpdatedAt     = DateTime.UtcNow,
                Message       = st,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "BillionPay status check failed for {Txn}", transactionId);
            return new PaymentStatusResult { TransactionId = transactionId, Status = tx.Status, Message = ex.Message };
        }
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default)
        => Task.FromResult(PaymentResult.Failed("Возвраты BillionPay — через их ЛК."));

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    private static string HmacSha512Hex(string key, string message)
    {
        using var h = new HMACSHA512(Encoding.UTF8.GetBytes(key));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private static string ReadBank(PaymentProviderConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.Settings))
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg.Settings);
                if (doc.RootElement.TryGetProperty("bank", out var b))
                {
                    var v = b.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                }
            }
            catch { /* fall through */ }
        }
        return "ANY_BANK";
    }
}
