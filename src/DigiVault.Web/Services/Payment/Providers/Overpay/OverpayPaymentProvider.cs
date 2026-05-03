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

namespace DigiVault.Web.Services.Payment.Providers.Overpay;

/// <summary>
/// <see cref="IPaymentProvider"/> for Overpay (api-pay.overpay.io). Two
/// authentication layers stack on every request:
///   1. HTTP Basic auth (Authorization header) - username + password
///   2. mTLS client certificate (.p12 with passphrase) - exchanged in the
///      TLS handshake by HttpClientHandler.ClientCertificates
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="overpay":
///   ApiKey      → HTTP Basic username (issued in LK)
///   SecretKey   → HTTP Basic password (issued in LK)
///   MerchantId  → projectId (e.g. "1084" for the sandbox project)
///   Settings    → JSON, e.g. { "certPath":"/var/www/digivault/secrets/overpay.p12",
///                              "certPass":"gF9...",
///                              "baseUrl":"https://api-pay.overpay.io" }
///
/// API endpoints used:
///   POST /orders/preflight   - create order, returns {id, resultUrl}
///   GET  /orders/{id}        - poll status (also used to verify webhooks
///                              since webhook body has no signature)
///
/// Webhook flow: Overpay POSTs {id, status, merchantTransactionId} to our
/// /api/webhooks/overpay endpoint. Body has no signature, so we re-verify
/// by calling GET /orders/{id} via mTLS - if the API confirms the same
/// status, we trust it. This makes IP-spoofed fake webhooks impossible:
/// the attacker would also need our client certificate to talk to the
/// upstream API.
/// </summary>
public class OverpayPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl = "https://api-pay.overpay.io";
    private const string HttpClientName = "overpay";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OverpayPaymentProvider> _log;

    public OverpayPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        ILogger<OverpayPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _log = log;
    }

    public string Name => "overpay";
    public string DisplayName => "Overpay";

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
                && !string.IsNullOrEmpty(cfg.MerchantId)   // projectId
                && !string.IsNullOrEmpty(cfg.ApiKey)        // basic-auth username
                && !string.IsNullOrEmpty(cfg.SecretKey);    // basic-auth password
        }
    }

    public bool SupportsRefund => true;

    // ────────────────────────────────────────────────────────────────────
    // Create payment
    // ────────────────────────────────────────────────────────────────────

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("Overpay не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("Overpay временно отключён.");
        if (string.IsNullOrEmpty(cfg.MerchantId))
            return PaymentResult.Failed("Overpay: не задан projectId (MerchantId).");
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey))
            return PaymentResult.Failed("Overpay: не заданы ApiKey + SecretKey (Basic auth).");

        // merchantTransactionId - наш внутренний txn-id, который Overpay вернёт
        // в webhook; ищем PaymentTransaction по нему.
        var ourTransactionId = "kz-" + Guid.NewGuid().ToString("N");

        // Map our high-level method to Overpay's paymentMethods enum:
        //   Card → "card"
        //   SBP  → "fps"
        var pmCode = request.Method switch
        {
            PaymentMethod.Card => "card",
            PaymentMethod.SBP  => "fps",
            _ => "card",
        };

        var payload = new Dictionary<string, object?>
        {
            ["amount"]                = request.Amount.ToString("0.##",
                                          System.Globalization.CultureInfo.InvariantCulture),
            ["currency"]              = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency,
            ["projectId"]             = cfg.MerchantId,
            ["merchantTransactionId"] = ourTransactionId,
            ["paymentMethods"]        = new[] { pmCode },
            ["livetimeMinutes"]       = 60,
            ["description"]           = request.Description ?? $"Order {ourTransactionId}",
            ["returnUrl"]             = request.SuccessUrl,
            ["client"]                = string.IsNullOrEmpty(request.Email)
                                            ? null
                                            : new Dictionary<string, string?> { ["email"] = request.Email },
        };

        // Strip nulls so the validator stays clean.
        var body = payload.Where(kv => kv.Value != null)
                          .ToDictionary(kv => kv.Key, kv => kv.Value!);

        var http = CreateHttpClient(cfg);
        var url  = ReadBaseUrl(cfg) + "/orders/preflight";

        try
        {
            _log.LogInformation(
                "Overpay → POST {Url} txn={Txn} amount={Amt} method={M}",
                url, ourTransactionId, request.Amount, pmCode);

            using var resp = await http.PostAsJsonAsync(url, body, ct);
            var responseText = await resp.Content.ReadAsStringAsync(ct);

            _log.LogInformation("Overpay ← {Code} {Body}", (int)resp.StatusCode, responseText);

            if (!resp.IsSuccessStatusCode)
                return PaymentResult.Failed(ExtractError(responseText) ?? "Overpay отказал в создании платежа",
                    ((int)resp.StatusCode).ToString());

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var overpayId = root.TryGetProperty("id",        out var idEl)  ? idEl.GetString()  ?? "" : "";
            var redirect  = root.TryGetProperty("resultUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(redirect))
            {
                _log.LogWarning("Overpay returned no resultUrl: {Body}", responseText);
                return PaymentResult.Failed("Overpay не вернул URL для редиректа");
            }

            return PaymentResult.Successful(
                transactionId:         ourTransactionId,
                redirectUrl:           redirect,
                providerTransactionId: overpayId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Overpay CreatePayment threw");
            return PaymentResult.Failed("Сетевая ошибка при обращении к Overpay");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Status check (also used to verify webhook authenticity)
    // ────────────────────────────────────────────────────────────────────

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        // We need Overpay's internal id to call /orders/{id}. Look up our
        // PaymentTransaction.ProviderTransactionId by the customer-facing
        // TransactionId we stored at create time.
        var txn = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);
        if (txn == null || string.IsNullOrEmpty(txn.ProviderTransactionId))
            return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Pending,
                                             Message = "Транзакция ещё не создана у провайдера" };

        var cfg = await LoadConfigAsync(ct);
        if (cfg == null)
            return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Failed,
                                             Message = "Overpay не настроен" };

        var http = CreateHttpClient(cfg);
        var url  = ReadBaseUrl(cfg) + $"/orders/{Uri.EscapeDataString(txn.ProviderTransactionId)}";

        try
        {
            using var resp = await http.GetAsync(url, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Overpay GetStatus failed ({Code}) for {Txn}: {Body}",
                    (int)resp.StatusCode, transactionId, text);
                return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Failed,
                                                 Message = ExtractError(text) };
            }

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var rawStatus = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            var amt = 0m;
            if (root.TryGetProperty("amount", out var a))
            {
                if (a.ValueKind == JsonValueKind.String) decimal.TryParse(a.GetString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out amt);
                else if (a.ValueKind == JsonValueKind.Number) amt = a.GetDecimal();
            }

            return new PaymentStatusResult
            {
                TransactionId = transactionId,
                Status        = MapStatus(rawStatus),
                Amount        = amt,
                Currency      = root.TryGetProperty("currency", out var c) ? (c.GetString() ?? "RUB") : "RUB",
                UpdatedAt     = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Overpay GetStatus threw");
            return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Failed,
                                             Message = "Сетевая ошибка" };
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Webhook validation - body has no signature, so we trust it only
    // after re-fetching status via mTLS.
    // ────────────────────────────────────────────────────────────────────

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        WebhookPayload? payload;
        try { payload = JsonSerializer.Deserialize<WebhookPayload>(body); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Overpay webhook body parse failed: {Body}", body);
            return WebhookValidationResult.Invalid("Невалидный JSON");
        }

        if (payload == null || string.IsNullOrEmpty(payload.Id) ||
            string.IsNullOrEmpty(payload.MerchantTransactionId))
            return WebhookValidationResult.Invalid("Webhook без id/merchantTransactionId");

        // Defense in depth: ignore body's status field, re-fetch authoritative
        // status from the API using our mTLS client. Any spoofed webhook fails
        // here because the attacker can't satisfy the cert handshake.
        var verify = await GetPaymentStatusAsync(payload.MerchantTransactionId, ct);

        return new WebhookValidationResult
        {
            IsValid       = true,
            TransactionId = payload.MerchantTransactionId,   // matches PaymentTransaction.TransactionId
            NewStatus     = verify.Status,
            Amount        = verify.Amount,
            RawData       = body,
        };
    }

    public async Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default)
    {
        var txn = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);
        if (txn == null || string.IsNullOrEmpty(txn.ProviderTransactionId))
            return PaymentResult.Failed("Не найдена связанная транзакция Overpay");

        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("Overpay не настроен");

        var http = CreateHttpClient(cfg);
        var url  = ReadBaseUrl(cfg) + $"/orders/{Uri.EscapeDataString(txn.ProviderTransactionId)}/refund";
        var body = new Dictionary<string, string?>
        {
            ["amount"] = (amount ?? txn.Amount).ToString("0.##",
                System.Globalization.CultureInfo.InvariantCulture),
        };

        try
        {
            using var req  = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return PaymentResult.Failed(ExtractError(text) ?? "Overpay отказал в возврате");
            return PaymentResult.Successful(transactionId, providerTransactionId: txn.ProviderTransactionId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Overpay refund threw");
            return PaymentResult.Failed("Сетевая ошибка при возврате");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    /// <summary>
    /// Builds an HttpClient with Basic auth header preset. The mTLS client
    /// certificate is attached at the handler level by the named-client
    /// factory configured in Program.cs (see AddHttpClient("overpay", …)).
    /// </summary>
    private HttpClient CreateHttpClient(PaymentProviderConfig cfg)
    {
        var http = _httpFactory.CreateClient(HttpClientName);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.ApiKey}:{cfg.SecretKey}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return http;
    }

    private static string ReadBaseUrl(PaymentProviderConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.Settings)) return DefaultBaseUrl;
        try
        {
            using var doc = JsonDocument.Parse(cfg.Settings);
            if (doc.RootElement.TryGetProperty("baseUrl", out var b))
            {
                var v = b.GetString();
                if (!string.IsNullOrWhiteSpace(v)) return v.TrimEnd('/');
            }
        }
        catch { /* ignore - fall back to default */ }
        return DefaultBaseUrl;
    }

    private static PaymentStatus MapStatus(string raw) => raw.ToLowerInvariant() switch
    {
        "charged"       => PaymentStatus.Completed,
        "authorized"    => PaymentStatus.Completed,   // 2-step flow auth → charge; we treat both as paid
        "credited"      => PaymentStatus.Completed,
        "rejected"      => PaymentStatus.Failed,
        "declined"      => PaymentStatus.Failed,
        "error"         => PaymentStatus.Failed,
        "reversed"      => PaymentStatus.Cancelled,
        "refunded"      => PaymentStatus.Refunded,
        "chargeback"    => PaymentStatus.Refunded,
        "representment" => PaymentStatus.Completed,   // chargeback resolved in our favour
        _               => PaymentStatus.Pending,     // preflight / new / processing / prepared / ...
    };

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var m)) return m.GetString();
            if (root.TryGetProperty("error",   out var e)) return e.GetString();
            if (root.TryGetProperty("detail",  out var d)) return d.GetString();
        }
        catch { }
        return null;
    }

    private sealed class WebhookPayload
    {
        public string? Id { get; set; }
        public string? Status { get; set; }
        public string? MerchantTransactionId { get; set; }
    }
}
