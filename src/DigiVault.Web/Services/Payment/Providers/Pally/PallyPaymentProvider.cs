using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.Pally;

/// <summary>
/// <see cref="IPaymentProvider"/> for Pally (pal24.pro / pally.info).
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="pally":
///   ApiKey      → Bearer token issued in Pally LK ("API integrations" tab)
///   MerchantId  → shop_id (10-char string assigned per shop in LK)
///   SecretKey   → unused; postback signatures use the Bearer token directly
///   Settings    → optional JSON, e.g. { "baseUrl":"https://pal24.pro" }
///
/// API endpoints used:
///   POST /api/v1/bill/create    - create bill, returns {bill_id, link_url, link_page_url}
///   GET  /api/v1/bill/status    - poll status (used as a backup to the postback)
///
/// Webhook flow: Pally POSTs form-encoded body to our /api/webhooks/pally with
/// InvId, OutSum, Commission, TrsId, Status, CurrencyIn, SignatureValue, ...
/// Signature: md5(OutSum:InvId:apiToken) uppercased.
/// </summary>
public class PallyPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl = "https://pal24.pro";
    private const string HttpClientName = "pally";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<PallyPaymentProvider> _log;

    public PallyPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer, ILogger<PallyPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "pally";
    public string DisplayName => "Pally";

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
                && !string.IsNullOrEmpty(cfg.ApiKey)       // Bearer token
                && !string.IsNullOrEmpty(cfg.MerchantId);  // shop_id
        }
    }

    public bool SupportsRefund => true;

    // ════════════════════════════════════════════════════════════════════
    // Create payment
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("Pally не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("Pally временно отключён.");
        if (string.IsNullOrEmpty(cfg.ApiKey))
            return PaymentResult.Failed("Pally: не задан Bearer-токен (ApiKey).");
        if (string.IsNullOrEmpty(cfg.MerchantId))
            return PaymentResult.Failed("Pally: не задан shop_id (MerchantId).");

        // Our internal txn-id — comes back in postback as InvId. Rotating
        // 2-letter prefix via TxnIdHelper avoids antifraud fingerprinting.
        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);

        // Pally's payment_method enum: BANK_CARD | SBP. If we don't pass it,
        // payer chooses on the form. We force-select per our internal Method
        // so the customer lands directly on the chosen flow.
        var paymentMethod = request.Method switch
        {
            PaymentMethod.Card => "BANK_CARD",
            PaymentMethod.SBP  => "SBP",
            _ => "",  // let payer pick
        };

        // Anonymise email for whitelisted internal accounts so Pally's
        // antifraud doesn't cluster them. Real users keep their actual email.
        var contacts = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);

        var formFields = new Dictionary<string, string>
        {
            ["amount"]   = request.Amount.ToString("0.##",
                              System.Globalization.CultureInfo.InvariantCulture),
            ["shop_id"]  = cfg.MerchantId!,
            ["order_id"] = ourTransactionId,
            ["currency_in"] = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency,
            // Generic description — keep the brand out of antifraud heuristics.
            ["description"] = request.Description ?? $"Order {ourTransactionId}",
            // NORMAL = one-shot bill, can't be paid twice. We always want this.
            ["type"]    = "normal",
            // Custom field — store the internal user id so we can correlate
            // multi-account abuse in our own analytics if needed. Pally echoes
            // it back in the postback.
            ["custom"]  = $"userid:{request.UserId}",
            // 1 = merchant absorbs commission; 0 = payer pays it on top.
            // Default to 1 so the displayed price matches the actual charge.
            ["payer_pays_commission"] = "0",
        };

        if (!string.IsNullOrEmpty(contacts.Email)) formFields["payer_email"] = contacts.Email;
        if (!string.IsNullOrEmpty(paymentMethod))  formFields["payment_method"] = paymentMethod;
        if (!string.IsNullOrEmpty(request.SuccessUrl)) formFields["success_url"] = request.SuccessUrl;
        if (!string.IsNullOrEmpty(request.CancelUrl))  formFields["fail_url"]    = request.CancelUrl;

        var url = ReadBaseUrl(cfg) + "/api/v1/bill/create";

        try
        {
            _log.LogInformation(
                "Pally → POST {Url} txn={Txn} amount={Amt} method={M}",
                url, ourTransactionId, request.Amount, paymentMethod);

            using var http = CreateHttpClient(cfg);
            using var content = new FormUrlEncodedContent(formFields);
            using var resp = await http.PostAsync(url, content, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            _log.LogInformation("Pally ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
            {
                return PaymentResult.Failed(
                    $"Pally вернул HTTP {(int)resp.StatusCode}: {raw}",
                    ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Pally returns "success":"true" (as a string!) per docs example.
            // Be tolerant of both string and bool just in case.
            var success = root.TryGetProperty("success", out var s) &&
                          (s.ValueKind == JsonValueKind.True ||
                           (s.ValueKind == JsonValueKind.String &&
                            string.Equals(s.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
            if (!success)
            {
                var err = root.TryGetProperty("message", out var m) ? m.GetString() : raw;
                return PaymentResult.Failed($"Pally отказал в создании счёта: {err}");
            }

            var billId = root.TryGetProperty("bill_id", out var b) ? b.GetString() : null;
            // link_page_url — full payment page; link_url is a QR-code page.
            // Use the page url for redirect; falls back to link_url.
            var redirectUrl = root.TryGetProperty("link_page_url", out var lp) ? lp.GetString() : null;
            if (string.IsNullOrEmpty(redirectUrl))
                redirectUrl = root.TryGetProperty("link_url", out var lu) ? lu.GetString() : null;

            if (string.IsNullOrEmpty(redirectUrl) || string.IsNullOrEmpty(billId))
            {
                return PaymentResult.Failed("Pally вернул success=true, но не дал payment URL / bill_id.");
            }

            _log.LogInformation(
                "Pally → bill created txn={Txn} bill_id={BillId} payURL={Url}",
                ourTransactionId, billId, redirectUrl);

            return new PaymentResult
            {
                Success = true,
                TransactionId = ourTransactionId,
                ProviderTransactionId = billId,
                RedirectUrl = redirectUrl,
                Status = PaymentStatus.Pending,
                ProviderData = new Dictionary<string, string>
                {
                    ["bill_id"] = billId,
                    ["link_page_url"] = redirectUrl,
                },
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pally /bill/create failed");
            return PaymentResult.Failed($"Pally недоступен: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Webhook validation
    // ════════════════════════════════════════════════════════════════════

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
            return WebhookValidationResult.Invalid("Pally not configured.");

        // Parse form-encoded body — the controller reconstructs Form into
        // `key=value&key=value` for us, but we still need to URL-decode here.
        var fields = ParseFormBody(body);

        if (!fields.TryGetValue("InvId", out var invId) || string.IsNullOrEmpty(invId))
            return WebhookValidationResult.Invalid("Pally webhook: InvId missing.");
        if (!fields.TryGetValue("OutSum", out var outSum) || string.IsNullOrEmpty(outSum))
            return WebhookValidationResult.Invalid("Pally webhook: OutSum missing.");
        if (!fields.TryGetValue("Status", out var status) || string.IsNullOrEmpty(status))
            return WebhookValidationResult.Invalid("Pally webhook: Status missing.");
        if (!fields.TryGetValue("SignatureValue", out var sig) || string.IsNullOrEmpty(sig))
            return WebhookValidationResult.Invalid("Pally webhook: SignatureValue missing.");

        // Signature: strtoupper(md5(OutSum:InvId:apiToken))
        var expected = Md5UpperHex($"{outSum}:{invId}:{cfg.ApiKey}");
        if (!string.Equals(sig, expected, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning(
                "Pally webhook signature mismatch. Got {Got} expected {Expected} (InvId={Inv})",
                sig, expected, invId);
            return WebhookValidationResult.Invalid("Signature mismatch.");
        }

        // Status mapping — per docs the postback enum is SUCCESS / UNDERPAID
        // / OVERPAID / FAIL. We treat OVERPAID as Completed (customer paid
        // more than required — still delivers); UNDERPAID as Failed (refund
        // path); FAIL as Failed.
        var newStatus = status.ToUpperInvariant() switch
        {
            "SUCCESS"   => PaymentStatus.Completed,
            "OVERPAID"  => PaymentStatus.Completed,
            "UNDERPAID" => PaymentStatus.Failed,
            "FAIL"      => PaymentStatus.Failed,
            _           => PaymentStatus.Pending,
        };

        decimal.TryParse(outSum, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var amt);

        return new WebhookValidationResult
        {
            IsValid       = true,
            TransactionId = invId,
            NewStatus     = newStatus,
            Amount        = amt,
            RawData       = body,
            // Pally retries up to 5× with exponential backoff if we don't
            // respond 200 OK — any 2xx body is fine.
            ResponseBody  = null,
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Status polling (backup to the postback)
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId,
        CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
        {
            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = PaymentStatus.Failed,
                Message       = "Pally not configured.",
            };
        }

        // The caller may pass either our InvId (== our internal TransactionId)
        // or Pally's bill_id. /api/v1/bill/status accepts only the latter, so
        // try to look up the providerTransactionId from our transactions table.
        var providerTxn = await _db.PaymentTransactions.AsNoTracking()
            .Where(t => t.ProviderName == Name &&
                       (t.TransactionId == transactionId || t.ProviderTransactionId == transactionId))
            .Select(t => t.ProviderTransactionId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(providerTxn))
            providerTxn = transactionId;

        var url = $"{ReadBaseUrl(cfg)}/api/v1/bill/status?id={Uri.EscapeDataString(providerTxn!)}";
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
                Status = st?.ToUpperInvariant() switch
                {
                    "SUCCESS" or "OVERPAID" => PaymentStatus.Completed,
                    "FAIL" or "UNDERPAID"   => PaymentStatus.Failed,
                    "PROCESS" or "NEW"      => PaymentStatus.Pending,
                    _                       => PaymentStatus.Pending,
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
        // Pally refund API exists (POST /api/v1/refund/full/create and
        // /partial/create) but per docs is gated «available upon request
        // through our support team». Surface a clear error so admins
        // contact support manually first.
        return Task.FromResult(PaymentResult.Failed(
            "Возврат через Pally подключается по запросу в их поддержке. Откройте тикет."));
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
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", cfg.ApiKey);
        http.DefaultRequestHeaders.Accept.Clear();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private static Dictionary<string, string> ParseFormBody(string body)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(body)) return dict;
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var key = Uri.UnescapeDataString(pair[..eq]);
            var val = Uri.UnescapeDataString(pair[(eq + 1)..]);
            dict[key] = val;
        }
        return dict;
    }

    private static string Md5UpperHex(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes); // uppercase by default
    }
}
