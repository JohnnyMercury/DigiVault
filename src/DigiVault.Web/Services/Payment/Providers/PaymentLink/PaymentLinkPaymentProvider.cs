using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.PaymentLink;

/// <summary>
/// <see cref="IPaymentProvider"/> for start.paymentlnk.com (Merchant Interface
/// v3.0). Unlike Enot, PaymentLink does not have a server-side «create
/// invoice» REST endpoint — the integration spec is a redirect-with-form to
/// <c>https://start.paymentlnk.com/api/payment/start</c> with all params
/// (incl. signature) sent in the POST body.
///
/// To fit our existing pattern (<see cref="PaymentResult.RedirectUrl"/> is a
/// GET-able URL), we generate the params + signature here, persist them as
/// JSON in <see cref="PaymentTransaction.ProviderData"/>, and return a
/// redirect to our local <c>/Payment/PaymentLinkStart/{transactionId}</c>
/// page. That page reads the JSON and renders an auto-submitting HTML form to
/// PaymentLink's endpoint.
///
/// Webhook handling: PaymentLink POSTs the same params back to our
/// configured callback (LK setting) — we receive it via the generic
/// <c>WebhooksController</c> route <c>/api/webhooks/paymentlink</c>, parse it
/// as application/x-www-form-urlencoded and verify the signature with the
/// same canonical-string rule.
///
/// Configuration in <see cref="PaymentProviderConfig"/> (Name="paymentlink"):
///   ApiKey      → секретный_ключ_1 (issued at registration)
///   SecretKey   → секретный_ключ_2 (set in LK)
///   MerchantId  → account (магазин-ID, shown in LK)
///   Settings    → optional JSON, e.g. {"algo":"hmac_sha256"} or {"algo":"md5"}
/// </summary>
public class PaymentLinkPaymentProvider : IPaymentProvider
{
    public const string TargetUrl     = "https://start.paymentlnk.com/api/payment/start";
    public const string TestTargetUrl = "https://start-test.paymentlnk.com/api/payment/start";

    private readonly ApplicationDbContext _db;
    private readonly ILogger<PaymentLinkPaymentProvider> _log;

    public PaymentLinkPaymentProvider(ApplicationDbContext db,
        ILogger<PaymentLinkPaymentProvider> log)
    {
        _db = db;
        _log = log;
    }

    public string Name => "paymentlink";
    public string DisplayName => "PaymentLink";

    /// <summary>
    /// PaymentLink supports cards (MBC) and an «alternative» bucket (EXT) that
    /// covers SBP / wallets / etc. We expose Card + SBP — our PaymentMethod
    /// catalog only needs those two right now.
    /// </summary>
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
                && !string.IsNullOrEmpty(cfg.MerchantId)
                && !string.IsNullOrEmpty(cfg.ApiKey);
        }
    }

    public bool SupportsRefund => false;

    // ────────────────────────────────────────────────────────────────────
    // Create payment — build params + signature, stash them, return our
    // redirect-page URL.
    // ────────────────────────────────────────────────────────────────────

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("PaymentLink не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("PaymentLink временно отключён.");
        if (string.IsNullOrEmpty(cfg.MerchantId))
            return PaymentResult.Failed("PaymentLink: не задан account (MerchantId).");
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey))
            return PaymentResult.Failed("PaymentLink: не заданы секретные ключи (ApiKey + SecretKey).");

        // Number must be ≤ 32 chars; allowed: 0-9 a-z A-Z а-я А-Я . - / space.
        // We use a short uuid to stay within budget and fit the alphabet.
        var ourTransactionId = "kz" + Guid.NewGuid().ToString("N").Substring(0, 24);

        var algo = ReadAlgo(cfg.Settings);
        var trtype = 1;
        var amountCurr = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency;
        // currency code in PaymentLink land: MBC for cards, EXT for everything
        // else (SBP / QR / wallets). Maps directly from our PaymentMethod.
        var plCurrency = request.Method == PaymentMethod.Card ? "MBC" : "EXT";
        var description = string.IsNullOrEmpty(request.Description)
            ? $"Order {ourTransactionId}"
            : request.Description;

        var signature = PaymentLinkSignatureHelper.Build(
            amount:      request.Amount,
            amountCurr:  amountCurr,
            currency:    plCurrency,
            number:      ourTransactionId,
            description: description,
            trtype:      trtype,
            account:     cfg.MerchantId,
            paytoken:    null,
            backUrl:     request.SuccessUrl,
            cf1:         request.OrderId?.ToString(),
            cf2:         request.UserId,
            cf3:         null,
            secretKey1:  cfg.ApiKey,
            secretKey2:  cfg.SecretKey,
            algo:        algo);

        // PaymentLink LK setting "Contact data are required" + their test
        // server reject the form with errorcode 311 ("There are no required
        // contact fields (phone, email)") if neither field is present. Ensure
        // we always send a non-empty email — fall back to a no-reply address
        // for guest checkouts where request.Email is null.
        var customerEmail = !string.IsNullOrWhiteSpace(request.Email)
            ? request.Email
            : "noreply@key-zona.com";

        // Form fields the redirect page will POST to PaymentLink. Stored as
        // JSON in ProviderData so the page can rebuild them by transactionId.
        var formFields = new Dictionary<string, string?>
        {
            ["amount"]      = request.Amount.ToString("0.##",
                                System.Globalization.CultureInfo.InvariantCulture),
            ["amountcurr"]  = amountCurr,
            ["currency"]    = plCurrency,
            ["number"]      = ourTransactionId,
            ["description"] = description,
            ["trtype"]      = trtype.ToString(),
            ["account"]     = cfg.MerchantId,
            ["backURL"]     = request.SuccessUrl,
            ["email"]       = customerEmail,
            ["lang"]        = "ru",
            ["cf1"]         = request.OrderId?.ToString(),
            ["cf2"]         = request.UserId,
            ["signature"]   = signature,
        };

        var providerData = JsonSerializer.Serialize(new
        {
            target = ReadTargetUrl(cfg),
            algo,
            form = formFields.Where(kv => !string.IsNullOrEmpty(kv.Value))
                             .ToDictionary(kv => kv.Key, kv => kv.Value!),
        });

        _log.LogInformation(
            "PaymentLink → prepared redirect for txn={Txn} amount={Amount} currency={Currency} method={Method}",
            ourTransactionId, request.Amount, plCurrency, request.Method);

        return new PaymentResult
        {
            Success               = true,
            TransactionId         = ourTransactionId,
            RedirectUrl           = $"/Payment/PaymentLinkStart/{ourTransactionId}",
            Status                = PaymentStatus.Pending,
            ProviderData          = new Dictionary<string, string> { ["data"] = providerData },
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Status check — PaymentLink has a /api/payment/status endpoint we could
    // call here, but the cheaper path (and the one webhook fires anyway) is
    // to trust the DB until the webhook arrives. We return Pending if no
    // transaction row exists yet, otherwise mirror its persisted status.
    // ────────────────────────────────────────────────────────────────────

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        var tx = await _db.PaymentTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId, ct);

        if (tx == null)
            return new PaymentStatusResult { TransactionId = transactionId, Status = PaymentStatus.Pending,
                                             Message = "Транзакция не найдена" };

        return new PaymentStatusResult
        {
            TransactionId = transactionId,
            Status        = tx.Status,
            Amount        = tx.Amount,
            Currency      = tx.Currency,
            UpdatedAt     = tx.UpdatedAt,
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // Webhook — application/x-www-form-urlencoded body posted by PaymentLink.
    // ────────────────────────────────────────────────────────────────────

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return WebhookValidationResult.Invalid("PaymentLink не настроен");
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey))
            return WebhookValidationResult.Invalid("PaymentLink: ключи не заданы");

        // Body comes as `key=val&key=val…` — parse with the same helper used
        // by ASP.NET form binding to avoid edge cases (URL-encoded values,
        // empty fields, etc.).
        var form = QueryHelpers.ParseQuery(body)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        var algo = ReadAlgo(cfg.Settings);

        if (!PaymentLinkSignatureHelper.VerifyConfirmationWebhook(form, cfg.ApiKey, cfg.SecretKey, algo))
        {
            _log.LogWarning("PaymentLink webhook signature mismatch. Body: {Body}", body);
            return WebhookValidationResult.Invalid("Неверная подпись webhook");
        }

        var number   = form.GetValueOrDefault("number") ?? "";
        var transId  = form.GetValueOrDefault("transID") ?? "";
        var opertype = (form.GetValueOrDefault("opertype") ?? "").ToLowerInvariant();

        // Map Payment Confirmation Request URL operations:
        //   pay         → payment is being approved, treat as Completed
        //   reversal    → refund / cancellation
        //   terminate   → capture of pre-authorised amount → Completed
        //   unblock     → release of held funds (effectively cancel)
        //   recurring   → scheduled recurring charge → Completed
        // PaymentLink expects us to respond with the `transID` value to
        // approve - any other body aborts the operation.
        var newStatus = opertype switch
        {
            "pay"       => PaymentStatus.Completed,
            "terminate" => PaymentStatus.Completed,
            "recurring" => PaymentStatus.Completed,
            "reversal"  => PaymentStatus.Refunded,
            "unblock"   => PaymentStatus.Cancelled,
            _           => PaymentStatus.Pending,
        };

        decimal amount = 0;
        decimal.TryParse(form.GetValueOrDefault("amount"),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out amount);

        _log.LogInformation(
            "PaymentLink webhook accepted: txn={Number} transID={TransId} opertype={OpType} → {Status}",
            number, transId, opertype, newStatus);

        return new WebhookValidationResult
        {
            IsValid       = true,
            TransactionId = number,
            NewStatus     = newStatus,
            Amount        = amount,
            RawData       = body,
            // CRITICAL: PaymentLink requires the response body to be exactly the
            // transID value (plain text, no JSON, no quotes). Any other body
            // causes them to abort the operation. The controller honors
            // ResponseBody when it sees a 200 OK with valid signature.
            ResponseBody  = transId,
        };
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default)
        => Task.FromResult(PaymentResult.Failed("Возвраты в PaymentLink делаются вручную через ЛК"));

    // ────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    private static string ReadAlgo(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return "hmac_sha256";
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.TryGetProperty("algo", out var a))
                return a.GetString() ?? "hmac_sha256";
        }
        catch { /* fall through */ }
        return "hmac_sha256";
    }

    internal static string ReadTargetUrl(PaymentProviderConfig cfg)
    {
        // 1. Explicit override in Settings JSON: {"targetUrl":"https://..."}
        if (!string.IsNullOrWhiteSpace(cfg.Settings))
        {
            try
            {
                using var doc = JsonDocument.Parse(cfg.Settings);
                if (doc.RootElement.TryGetProperty("targetUrl", out var t))
                {
                    var v = t.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v.TrimEnd('/');
                }
            }
            catch { /* fall through */ }
        }
        // 2. IsTestMode flag → use test endpoint automatically
        return cfg.IsTestMode ? TestTargetUrl : TargetUrl;
    }

}
