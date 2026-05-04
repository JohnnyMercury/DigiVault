using System.Text;
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
/// v3.0). Supports two flows depending on payment method:
///
///   • Card (currency=MBC) — redirect-with-form to /api/payment/start. The
///     customer's browser auto-submits the form to PL's hosted checkout. We
///     stash the prepared params in PaymentTransaction.ProviderData and
///     return a redirect to /Payment/PaymentLinkStart/{txId} which renders
///     the form.
///
///   • SBP / EXT (currency=EXT, paysys=EXT in invoice flow) — server-to-
///     server POST to /api/payment/invoice. PL responds with a payURL we
///     redirect the customer to. PL's confirmation webhook is NOT used in
///     this flow (per PL support: "в этом режиме интеграции отсутствуют
///     наши запросы на подтверждение"); we only get the statusURL webhook
///     after a successful charge, and rely on /api/payment/operate polling
///     for wait → error transitions.
///
/// Configuration in <see cref="PaymentProviderConfig"/> (Name="paymentlink"):
///   ApiKey      → секретный_ключ_1 (issued at registration)
///   SecretKey   → секретный_ключ_2 (set in LK)
///   MerchantId  → account (магазин-ID, shown in LK)
///   Settings    → JSON; supports {"algo":"md5"|"hmac_sha256"} and
///                 {"baseUrl":"https://..."} (override host for test/staging).
/// </summary>
public class PaymentLinkPaymentProvider : IPaymentProvider
{
    public const string BaseUrl     = "https://start.paymentlnk.com";
    public const string TestBaseUrl = "https://start-test.paymentlnk.com";

    // Backward compat — referenced by Views/Payment/PaymentLinkStart.cshtml
    // and existing tests/logging.
    public const string TargetUrl     = BaseUrl     + "/api/payment/start";
    public const string TestTargetUrl = TestBaseUrl + "/api/payment/start";

    private const string HttpClientName = "paymentlink";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PaymentLinkPaymentProvider> _log;

    public PaymentLinkPaymentProvider(
        ApplicationDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<PaymentLinkPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _log = log;
    }

    public string Name => "paymentlink";
    public string DisplayName => "PaymentLink";

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

    // ════════════════════════════════════════════════════════════════════
    // CreatePaymentAsync — dispatches Card → /payment/start, SBP → /payment/invoice
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("PaymentLink не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("PaymentLink временно отключён.");
        if (string.IsNullOrEmpty(cfg.MerchantId))
            return PaymentResult.Failed("PaymentLink: не задан account (MerchantId).");
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey))
            return PaymentResult.Failed("PaymentLink: не заданы секретные ключи (ApiKey + SecretKey).");

        return request.Method == PaymentMethod.SBP
            ? await CreateInvoicePaymentAsync(cfg, request, ct)
            : await CreateRedirectPaymentAsync(cfg, request, ct);
    }

    // ────────────────────────────────────────────────────────────────────
    // /api/payment/start flow — used for cards (MBC). Browser auto-submits.
    // ────────────────────────────────────────────────────────────────────

    private async Task<PaymentResult> CreateRedirectPaymentAsync(
        PaymentProviderConfig cfg, PaymentRequest request, CancellationToken ct)
    {
        // Number must be ≤ 32 chars; allowed: 0-9 a-z A-Z а-я А-Я . - / space.
        var ourTransactionId = TxnIdHelper.Generate(maxLength: 28);

        var algo = ReadAlgo(cfg.Settings);
        var trtype = 1;
        var amountCurr = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency;
        var plCurrency = "MBC"; // card flow only
        var description = string.IsNullOrEmpty(request.Description)
            ? $"Order {ourTransactionId}"
            : request.Description;

        // cf1 carries the buyer id in PaymentLink's expected format.
        var cf1Value = $"userid:{request.UserId}";

        var signature = PaymentLinkSignatureHelper.Build(
            amount:      request.Amount,
            amountCurr:  amountCurr,
            currency:    plCurrency,
            number:      ourTransactionId,
            description: description,
            trtype:      trtype,
            account:     cfg.MerchantId!,
            paytoken:    null,
            backUrl:     request.SuccessUrl,
            cf1:         cf1Value,
            cf2:         null,
            cf3:         null,
            secretKey1:  cfg.ApiKey!,
            secretKey2:  cfg.SecretKey!,
            algo:        algo);

        var customerEmail = !string.IsNullOrWhiteSpace(request.Email)
            ? request.Email
            : "noreply@key-zona.com";

        var customerPhone = NormalizePhone(request.Phone);

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
            ["phone"]       = customerPhone,
            ["lang"]        = "ru",
            ["cf1"]         = cf1Value,
            ["signature"]   = signature,
        };

        _log.LogInformation(
            "PaymentLink /start fields: {Fields}",
            string.Join(", ", formFields.Where(kv => kv.Key != "signature")
                                        .Select(kv => $"{kv.Key}={kv.Value}")));

        var providerData = JsonSerializer.Serialize(new
        {
            target = ReadBaseUrl(cfg) + "/api/payment/start",
            algo,
            form = formFields.Where(kv => !string.IsNullOrEmpty(kv.Value))
                             .ToDictionary(kv => kv.Key, kv => kv.Value!),
        });

        _log.LogInformation(
            "PaymentLink → prepared /start redirect for txn={Txn} amount={Amount}",
            ourTransactionId, request.Amount);

        return new PaymentResult
        {
            Success       = true,
            TransactionId = ourTransactionId,
            RedirectUrl   = $"/Payment/PaymentLinkStart/{ourTransactionId}",
            Status        = PaymentStatus.Pending,
            ProviderData  = new Dictionary<string, string> { ["data"] = providerData },
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // /api/payment/invoice flow — used for SBP / EXT. Server-to-server.
    // ────────────────────────────────────────────────────────────────────

    private async Task<PaymentResult> CreateInvoicePaymentAsync(
        PaymentProviderConfig cfg, PaymentRequest request, CancellationToken ct)
    {
        var ourTransactionId = TxnIdHelper.Generate(maxLength: 28);

        var algo = ReadAlgo(cfg.Settings);
        var amountCurr = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency;
        var paysys = "EXT"; // SBP / alternative methods bucket
        var description = string.IsNullOrEmpty(request.Description)
            ? $"Order {ourTransactionId}"
            : request.Description;
        var firstName = "Покупатель"; // PL spec allows static placeholder name
        // validity: how long PL keeps the invoice payable. 60 min is enough
        // for SBP (user opens bank app, scans QR, confirms).
        var validity = DateTime.UtcNow.AddMinutes(60).ToString("yyyy-MM-ddTHH:mm:sszzz",
            System.Globalization.CultureInfo.InvariantCulture);

        var cf1Value = $"userid:{request.UserId}";
        var customerEmail = !string.IsNullOrWhiteSpace(request.Email)
            ? request.Email
            : "noreply@key-zona.com";
        var customerPhone = NormalizePhone(request.Phone);

        var signature = PaymentLinkSignatureHelper.BuildInvoice(
            amount:       request.Amount,
            amountCurr:   amountCurr,
            paysys:       paysys,
            number:       ourTransactionId,
            description:  description,
            validity:     validity,
            firstName:    firstName,
            lastName:     null,
            middleName:   null,
            cf1:          cf1Value,
            cf2:          null,
            cf3:          null,
            email:        customerEmail,
            notifyEmail:  "0",
            phone:        customerPhone,
            notifyPhone:  "0",
            paytoken:     null,
            backUrl:      request.SuccessUrl,
            account:      cfg.MerchantId!,
            secretKey1:   cfg.ApiKey!,
            secretKey2:   cfg.SecretKey!,
            algo:         algo);

        var formFields = new Dictionary<string, string?>
        {
            ["amount"]       = request.Amount.ToString("0.##",
                                  System.Globalization.CultureInfo.InvariantCulture),
            ["amountcurr"]   = amountCurr,
            ["paysys"]       = paysys,
            ["number"]       = ourTransactionId,
            ["description"]  = description,
            ["validity"]     = validity,
            ["first_name"]   = firstName,
            ["email"]        = customerEmail,
            ["notify_email"] = "0",
            ["phone"]        = customerPhone,
            ["notify_phone"] = "0",
            ["account"]      = cfg.MerchantId,
            ["backURL"]      = request.SuccessUrl,
            ["cf1"]          = cf1Value,
            ["signature"]    = signature,
        };

        _log.LogInformation(
            "PaymentLink /invoice fields: {Fields}",
            string.Join(", ", formFields.Where(kv => kv.Key != "signature")
                                        .Select(kv => $"{kv.Key}={kv.Value}")));

        var url = ReadBaseUrl(cfg) + "/api/payment/invoice";
        var http = _httpFactory.CreateClient(HttpClientName);

        try
        {
            var body = new FormUrlEncodedContent(
                formFields.Where(kv => kv.Value != null)!
                          .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value!))!);
            using var resp = await http.PostAsync(url, body, ct);
            var responseText = await resp.Content.ReadAsStringAsync(ct);

            _log.LogInformation("PaymentLink ← /invoice {Code} {Body}", (int)resp.StatusCode, responseText);

            if (!resp.IsSuccessStatusCode)
                return PaymentResult.Failed(
                    $"PaymentLink /invoice вернул {(int)resp.StatusCode}",
                    ((int)resp.StatusCode).ToString());

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? (s.GetString() ?? "") : "";

            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            {
                var errCode = root.TryGetProperty("errorcode", out var ec) ? ec.GetString() : null;
                var errText = root.TryGetProperty("errortext", out var et) ? et.GetString() : null;
                return PaymentResult.Failed(
                    string.IsNullOrEmpty(errText) ? "PaymentLink отказал в выставлении счёта" : errText,
                    errCode);
            }

            // status == "wait" — счёт выставлен, ждём оплаты.
            var transId = root.TryGetProperty("transID", out var ti) ? ti.GetString() : null;
            var payUrl  = root.TryGetProperty("payURL",  out var pu) ? pu.GetString() : null;

            if (string.IsNullOrEmpty(payUrl))
                return PaymentResult.Failed("PaymentLink не вернул payURL для редиректа");

            _log.LogInformation(
                "PaymentLink → /invoice prepared txn={Txn} transID={TransId} payURL={PayUrl}",
                ourTransactionId, transId, payUrl);

            return PaymentResult.Successful(
                transactionId:         ourTransactionId,
                redirectUrl:           payUrl,
                providerTransactionId: transId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PaymentLink /invoice threw");
            return PaymentResult.Failed("Сетевая ошибка при обращении к PaymentLink");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // GetPaymentStatusAsync — used by /Account/Order pages. Reads from DB
    // (poller service does the actual /operate calls).
    // ════════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════════
    // Webhooks
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Confirmation webhook (LK setting "Payment Confirmation Request URL").
    /// PaymentLink POSTs this BEFORE charging. Response must be the literal
    /// transID; any other body aborts the operation with errcode=130.
    /// </summary>
    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return WebhookValidationResult.Invalid("PaymentLink не настроен");
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey))
            return WebhookValidationResult.Invalid("PaymentLink: ключи не заданы");

        var form = QueryHelpers.ParseQuery(body)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        var algo = ReadAlgo(cfg.Settings);

        if (!PaymentLinkSignatureHelper.VerifyConfirmationWebhook(form, cfg.ApiKey, cfg.SecretKey, algo))
        {
            _log.LogWarning("PaymentLink confirmation webhook signature mismatch. Body: {Body}", body);
            return WebhookValidationResult.Invalid("Неверная подпись webhook");
        }

        var number   = form.GetValueOrDefault("number") ?? "";
        var transId  = form.GetValueOrDefault("transID") ?? "";
        var opertype = (form.GetValueOrDefault("opertype") ?? "").ToLowerInvariant();

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
            "PaymentLink confirmation webhook accepted: txn={Number} transID={TransId} opertype={OpType} → {Status}",
            number, transId, opertype, newStatus);

        return new WebhookValidationResult
        {
            IsValid       = true,
            TransactionId = number,
            NewStatus     = newStatus,
            Amount        = amount,
            RawData       = body,
            ResponseBody  = transId,
        };
    }

    /// <summary>
    /// Status webhook (LK setting "Payment Status Update URL"). PaymentLink
    /// POSTs this AFTER a successful charge. Response must be the literal
    /// "OK"; any other body causes them to retry the notification for hours.
    /// Different signature canonical from the confirmation webhook.
    /// </summary>
    public async Task<WebhookValidationResult> ValidateStatusWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return WebhookValidationResult.Invalid("PaymentLink не настроен");
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey))
            return WebhookValidationResult.Invalid("PaymentLink: ключи не заданы");

        var form = QueryHelpers.ParseQuery(body)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        var algo = ReadAlgo(cfg.Settings);

        if (!PaymentLinkSignatureHelper.VerifyStatusWebhook(form, cfg.ApiKey, cfg.SecretKey, algo))
        {
            _log.LogWarning("PaymentLink status webhook signature mismatch. Body: {Body}", body);
            return WebhookValidationResult.Invalid("Неверная подпись status-webhook");
        }

        var number  = form.GetValueOrDefault("number") ?? "";
        var transId = form.GetValueOrDefault("transID") ?? "";

        decimal amount = 0;
        decimal.TryParse(form.GetValueOrDefault("payamount") ?? form.GetValueOrDefault("amount"),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out amount);

        _log.LogInformation(
            "PaymentLink status webhook accepted: txn={Number} transID={TransId} → Completed",
            number, transId);

        // Status webhook only fires after successful settlement → Completed.
        return new WebhookValidationResult
        {
            IsValid       = true,
            TransactionId = number,
            NewStatus     = PaymentStatus.Completed,
            Amount        = amount,
            RawData       = body,
            ResponseBody  = "OK", // спецификация: «должны быть возвращены символы OK»
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Status polling — used by PaymentLinkStatusPollerService for txns where
    // no callback has arrived (PL only pushes callbacks for successes).
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Polls PaymentLink's /api/payment/operate with opertype=check for the
    /// given provider transID. Returns the parsed status (OK/error/wait/etc).
    /// Caller is responsible for applying status mapping + DB updates.
    /// </summary>
    public async Task<PaymentLinkOperateResult> PollOperateStatusAsync(
        string providerTransactionId, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null)
            return new PaymentLinkOperateResult { Status = "error", ErrorText = "PaymentLink не настроен" };
        if (string.IsNullOrEmpty(cfg.ApiKey) || string.IsNullOrEmpty(cfg.SecretKey) ||
            string.IsNullOrEmpty(cfg.MerchantId))
            return new PaymentLinkOperateResult { Status = "error", ErrorText = "PaymentLink: ключи не заданы" };

        var algo = ReadAlgo(cfg.Settings);
        var signature = PaymentLinkSignatureHelper.BuildOperateCheck(
            opertype:   "check",
            account:    cfg.MerchantId,
            transId:    providerTransactionId,
            secretKey1: cfg.ApiKey,
            secretKey2: cfg.SecretKey,
            algo:       algo);

        var url = ReadBaseUrl(cfg) + "/api/payment/operate";
        var http = _httpFactory.CreateClient(HttpClientName);

        try
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("opertype",  "check"),
                new KeyValuePair<string, string>("account",   cfg.MerchantId),
                new KeyValuePair<string, string>("transID",   providerTransactionId),
                new KeyValuePair<string, string>("signature", signature),
            });

            using var resp = await http.PostAsync(url, body, ct);
            var responseText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new PaymentLinkOperateResult
                {
                    Status    = "error",
                    ErrorText = $"HTTP {(int)resp.StatusCode}: {responseText}",
                };

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            var status = (root.TryGetProperty("status", out var s) ? s.GetString() : null) ?? "";
            decimal finalAmount = 0;
            if (root.TryGetProperty("finalamount", out var fa))
            {
                if (fa.ValueKind == JsonValueKind.String)
                    decimal.TryParse(fa.GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out finalAmount);
                else if (fa.ValueKind == JsonValueKind.Number)
                    finalAmount = fa.GetDecimal();
            }

            return new PaymentLinkOperateResult
            {
                Status      = status,
                TransId     = root.TryGetProperty("transID",  out var ti) ? ti.GetString() : null,
                Number      = root.TryGetProperty("number",   out var n)  ? n.GetString()  : null,
                FinalAmount = finalAmount,
                ErrorCode   = root.TryGetProperty("errorcode", out var ec) ? ec.GetString() : null,
                ErrorText   = root.TryGetProperty("errortext", out var et) ? et.GetString() : null,
                Raw         = responseText,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PaymentLink /operate threw for transID={TransId}", providerTransactionId);
            return new PaymentLinkOperateResult { Status = "error", ErrorText = ex.Message };
        }
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default)
        => Task.FromResult(PaymentResult.Failed("Возвраты в PaymentLink делаются вручную через ЛК"));

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    private static string ReadAlgo(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return "md5";
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.TryGetProperty("algo", out var a))
                return a.GetString() ?? "md5";
        }
        catch { /* fall through */ }
        return "md5";
    }

    /// <summary>
    /// Reads the protocol+host base for PaymentLink endpoints. Settings JSON
    /// can override via <c>{"baseUrl":"https://..."}</c>; for backward
    /// compatibility we also recognise the older <c>"targetUrl"</c> key by
    /// stripping its <c>/api/payment/start</c> suffix. Otherwise selects test
    /// vs prod based on <see cref="PaymentProviderConfig.IsTestMode"/>.
    /// </summary>
    internal static string ReadBaseUrl(PaymentProviderConfig cfg)
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
                if (doc.RootElement.TryGetProperty("targetUrl", out var t))
                {
                    var v = (t.GetString() ?? "").TrimEnd('/');
                    const string startSuffix = "/api/payment/start";
                    if (v.EndsWith(startSuffix, StringComparison.OrdinalIgnoreCase))
                        v = v.Substring(0, v.Length - startSuffix.Length);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { /* fall through */ }
        }
        return cfg.IsTestMode ? TestBaseUrl : BaseUrl;
    }

    /// <summary>Compatibility shim — used by PaymentLinkStart.cshtml view.</summary>
    internal static string ReadTargetUrl(PaymentProviderConfig cfg)
        => ReadBaseUrl(cfg) + "/api/payment/start";

    private static string NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "79000000000";
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("8"))
            digits = "7" + digits.Substring(1);
        return string.IsNullOrEmpty(digits) ? "79000000000" : digits;
    }
}

/// <summary>Lightweight DTO for /api/payment/operate poll responses.</summary>
public sealed class PaymentLinkOperateResult
{
    public string Status { get; set; } = "";
    public string? TransId { get; set; }
    public string? Number { get; set; }
    public decimal FinalAmount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorText { get; set; }
    public string? Raw { get; set; }
}
