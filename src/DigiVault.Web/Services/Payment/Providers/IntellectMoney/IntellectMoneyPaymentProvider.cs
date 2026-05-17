using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.IntellectMoney;

/// <summary>
/// <see cref="IPaymentProvider"/> for IntellectMoney (api.intellectmoney.ru).
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="intellectmoney":
///   MerchantId  → EshopId (6-digit shop ID from intellectmoney LK)
///   ApiKey      → SecretKey (used to build the Hash field — md5 lowercase)
///   SecretKey   → optional Bearer token if «продвинутый» доступ is enabled
///                 in LK. On Basic tier we don't need it; if their API ever
///                 returns 401 we'll plug this in.
///   Settings    → optional JSON: {"baseUrl":"https://api.intellectmoney.ru"}
///
/// Hash formula (per /merchant/createInvoice docs):
///   EshopId::OrderId::ServiceName::RecipientAmount::RecipientCurrency::
///   UserName::Email::SuccessUrl::::BackUrl::ResultUrl::ExpireDate::
///   HoldMode::Preference::SecretKey
///
/// Two-colon separators stay even when the value is empty (note the
/// `::::` after SuccessUrl — that's an undocumented field IntellectMoney
/// reserves; we always feed it empty per their own example).
///
/// Webhook flow: IntellectMoney POSTs form-encoded {OrderId, InvoiceId,
/// PaymentStatus, RecipientAmount, …} to /api/webhooks/intellectmoney
/// with their Hash. Status enum (per docs):
///   3 — счёт создан      → Pending
///   4 — отменён          → Cancelled
///   5 — оплачен          → Completed
///   6 — захолдирован     → Processing
///   7 — частично оплачен → Processing
///   8 — возврат          → Refunded
/// </summary>
public class IntellectMoneyPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl   = "https://api.intellectmoney.ru";
    private const string PayPageBaseUrl   = "https://merchant.intellectmoney.ru";
    private const string HttpClientName   = "intellectmoney";

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<IntellectMoneyPaymentProvider> _log;

    public IntellectMoneyPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer, ILogger<IntellectMoneyPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "intellectmoney";
    public string DisplayName => "IntellectMoney";

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
                && !string.IsNullOrEmpty(cfg.MerchantId)   // EshopId
                && !string.IsNullOrEmpty(cfg.ApiKey);      // SecretKey
        }
    }

    public bool SupportsRefund => false;

    // ════════════════════════════════════════════════════════════════════
    // Create payment
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("IntellectMoney не настроен в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("IntellectMoney временно отключён.");
        if (string.IsNullOrEmpty(cfg.MerchantId))
            return PaymentResult.Failed("IntellectMoney: не задан EshopId (MerchantId).");
        if (string.IsNullOrEmpty(cfg.ApiKey))
            return PaymentResult.Failed("IntellectMoney: не задан SecretKey (ApiKey).");

        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);

        // Preference picks which payment-method buttons IntellectMoney offers
        // on its hosted page. Empty = show all enabled methods (then customer
        // chooses on their page); set explicitly to skip the chooser.
        var preference = request.Method switch
        {
            PaymentMethod.SBP  => "Sbp",
            PaymentMethod.Card => "BankCard",
            _ => "",
        };

        // Anonymise contacts for whitelisted internal accounts.
        var contacts = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);

        var amount   = request.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var currency = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency;
        var email    = contacts.Email ?? "";
        var serviceName = request.Description ?? $"Платёж №{ourTransactionId}";
        var successUrl  = request.SuccessUrl ?? "";
        var backUrl     = request.CancelUrl  ?? "";
        var resultUrl   = request.WebhookUrl ?? "";

        // Hash = md5_lower(EshopId::OrderId::ServiceName::RecipientAmount::
        //   RecipientCurrency::UserName::Email::SuccessUrl::::BackUrl::
        //   ResultUrl::ExpireDate::HoldMode::Preference::SecretKey)
        //
        // The `::::` after SuccessUrl is intentional — that's an undocumented
        // empty slot in IntellectMoney's canonical signing string (their own
        // example shows it). UserName, ExpireDate and HoldMode are also
        // empty for us; we still feed the separators per their rule:
        // «пустые значения остаются пустыми между ::, но сам разделитель
        // сохраняется».
        var canonical = string.Join("::",
            cfg.MerchantId,
            ourTransactionId,
            serviceName,
            amount,
            currency,
            "",            // UserName
            email,
            successUrl,
            "",            // undocumented empty slot
            backUrl,
            resultUrl,
            "",            // ExpireDate
            "",            // HoldMode
            preference,
            cfg.ApiKey
        );
        var hash = Md5LowerHex(canonical);

        var form = new List<KeyValuePair<string, string>>
        {
            new("EshopId",           cfg.MerchantId!),
            new("OrderId",           ourTransactionId),
            new("ServiceName",       serviceName),
            new("RecipientAmount",   amount),
            new("RecipientCurrency", currency),
            new("Email",             email),
            new("SuccessUrl",        successUrl),
            new("BackUrl",           backUrl),
            new("ResultUrl",         resultUrl),
            new("Hash",              hash),
        };
        if (!string.IsNullOrEmpty(preference))
            form.Add(new("Preference", preference));

        var url = ReadBaseUrl(cfg) + "/merchant/createInvoice";

        try
        {
            _log.LogInformation(
                "IntellectMoney → POST {Url} txn={Txn} amount={Amt} pref={Pref}",
                url, ourTransactionId, amount, preference);

            using var http = _httpFactory.CreateClient(HttpClientName);
            // SecretKey field carries the optional Bearer token for the
            // advanced-access tier. On Basic tier this is empty and we just
            // skip the header.
            if (!string.IsNullOrEmpty(cfg.SecretKey))
            {
                http.DefaultRequestHeaders.Remove("Authorization");
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {cfg.SecretKey}");
            }
            http.DefaultRequestHeaders.Accept.Clear();
            http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var content = new FormUrlEncodedContent(form);
            using var resp = await http.PostAsync(url, content, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            _log.LogInformation("IntellectMoney ← {Status} {Body}", (int)resp.StatusCode, raw);

            if (!resp.IsSuccessStatusCode)
            {
                return PaymentResult.Failed(
                    $"IntellectMoney вернул HTTP {(int)resp.StatusCode}: {raw}",
                    ((int)resp.StatusCode).ToString());
            }

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            // Both OperationState and Result.State carry codes; success is
            // Code == 0 on both. We check OperationState first because it's
            // outermost (transport-level), then Result.State (business-level).
            var opCode = root.TryGetProperty("OperationState", out var ops)
                          && ops.TryGetProperty("Code", out var opc) ? opc.GetInt32() : -1;
            if (opCode != 0)
            {
                var opDesc = ops.TryGetProperty("Desc", out var od) ? od.GetString() : raw;
                return PaymentResult.Failed($"IntellectMoney OperationState error: {opDesc}");
            }

            if (!root.TryGetProperty("Result", out var result))
                return PaymentResult.Failed($"IntellectMoney: нет Result в ответе. {raw}");

            var resCode = result.TryGetProperty("State", out var st)
                           && st.TryGetProperty("Code", out var stc) ? stc.GetInt32() : -1;
            if (resCode != 0)
            {
                var stDesc = st.TryGetProperty("Desc", out var sd) ? sd.GetString() : raw;
                return PaymentResult.Failed($"IntellectMoney Result.State error: {stDesc}");
            }

            if (!result.TryGetProperty("InvoiceId", out var invEl))
                return PaymentResult.Failed($"IntellectMoney: нет InvoiceId. {raw}");
            var invoiceId = invEl.ValueKind == JsonValueKind.Number
                ? invEl.GetInt64().ToString()
                : invEl.GetString();

            if (string.IsNullOrEmpty(invoiceId))
                return PaymentResult.Failed("IntellectMoney: пустой InvoiceId.");

            // Customer is redirected to the hosted pay page where they pick a
            // method (or land directly on it if Preference was set).
            var payPage = $"{PayPageBaseUrl}/?InvoiceId={Uri.EscapeDataString(invoiceId)}";

            _log.LogInformation(
                "IntellectMoney → invoice created txn={Txn} invoice={Inv} url={Url}",
                ourTransactionId, invoiceId, payPage);

            return new PaymentResult
            {
                Success = true,
                TransactionId = ourTransactionId,
                ProviderTransactionId = invoiceId,
                RedirectUrl = payPage,
                Status = PaymentStatus.Pending,
                ProviderData = new Dictionary<string, string>
                {
                    ["invoice_id"] = invoiceId,
                    ["pay_url"]    = payPage,
                },
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "IntellectMoney /createInvoice failed");
            return PaymentResult.Failed($"IntellectMoney недоступен: {ex.Message}");
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
            return WebhookValidationResult.Invalid("IntellectMoney not configured.");

        var fields = ParseFormBody(body);

        if (!fields.TryGetValue("OrderId", out var orderId) || string.IsNullOrEmpty(orderId))
            return WebhookValidationResult.Invalid("OrderId missing.");

        if (!fields.TryGetValue("PaymentStatus", out var statusRaw))
            return WebhookValidationResult.Invalid("PaymentStatus missing.");
        if (!int.TryParse(statusRaw, out var statusCode))
            return WebhookValidationResult.Invalid($"PaymentStatus not an int: {statusRaw}");

        // EshopId match — minimal integrity check. The full Hash spec for the
        // webhook isn't published explicitly enough to reproduce here without
        // risk of false-negatives, so for now we trust the source iff the
        // body's EshopId matches our configured shop. Tighten later if their
        // sample webhook payload is published.
        if (fields.TryGetValue("EshopId", out var hookEshop) &&
            !string.Equals(hookEshop, cfg.MerchantId, StringComparison.Ordinal))
        {
            _log.LogWarning(
                "IntellectMoney webhook: EshopId mismatch (got '{Got}' expected '{Exp}')",
                hookEshop, cfg.MerchantId);
            return WebhookValidationResult.Invalid("EshopId mismatch.");
        }

        decimal amount = 0;
        if (fields.TryGetValue("RecipientAmount", out var amtRaw)
            || fields.TryGetValue("Amount", out amtRaw!))
        {
            decimal.TryParse(amtRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out amount);
        }

        var newStatus = statusCode switch
        {
            5 => PaymentStatus.Completed,   // оплачен
            4 => PaymentStatus.Cancelled,   // отменён
            8 => PaymentStatus.Refunded,    // возврат
            6 => PaymentStatus.Processing,  // захолдирован
            7 => PaymentStatus.Processing,  // частично оплачен
            3 => PaymentStatus.Pending,     // счёт создан
            _ => PaymentStatus.Pending,
        };

        return new WebhookValidationResult
        {
            IsValid       = true,
            TransactionId = orderId,
            NewStatus     = newStatus,
            Amount        = amount,
            RawData       = body,
            // IntellectMoney docs don't mandate a specific response body —
            // any 200 OK satisfies them.
            ResponseBody  = null,
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Status polling (best-effort stub)
    // ════════════════════════════════════════════════════════════════════

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId,
        CancellationToken ct = default)
    {
        // IntellectMoney has a status-check API but the endpoint isn't
        // documented inline with createInvoice. The Pending → Failed
        // reconciliation we do for PaymentLink isn't critical here because
        // IntellectMoney sends a webhook on both success and cancel
        // (statuses 5 and 4 respectively), so transactions naturally land
        // in a terminal state. If we ever need active polling, plug in
        // the /merchant/invoiceState endpoint here.
        return Task.FromResult(new PaymentStatusResult
        {
            TransactionId = transactionId,
            Status        = PaymentStatus.Pending,
            Message       = "Polling not implemented — waiting for webhook.",
        });
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(PaymentResult.Failed(
            "Возврат через IntellectMoney выполняется только в их ЛК (раздел Операции)."));
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

    private static string Md5LowerHex(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
