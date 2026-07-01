using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.FreeKassa;

/// <summary>
/// <see cref="IPaymentProvider"/> for FreeKassa (api.fk.life, API v1 — NOT SCI).
///
/// Configuration in <see cref="PaymentProviderConfig"/> with Name="freekassa":
///   MerchantId  → shopId (числовой ID магазина, здесь "74374")
///   ApiKey      → API-ключ со страницы настроек ЛК. Им подписываются
///                 исходящие запросы: HMAC-SHA256 по значениям параметров,
///                 отсортированным по ключу и склеенным через '|'.
///   SecretKey   → «Секретное слово 2». Им проверяется подпись вебхука:
///                 md5(shopId:amount:secretWord2:orderId).
///   Settings    → JSON, например:
///                 {
///                   "baseUrl":"https://api.fk.life/v1",
///                   "secretWord1":"...",   // для legacy SCI, в API не нужен
///                   "fallbackIp":"145.223.90.75",   // если у клиента нет IP
///                   "i_card":36, "i_sbp":44, "i_sberpay":null
///                 }
///
/// Flow:
///   POST {baseUrl}/orders/create → { type:"success", orderId, location } —
///   ссылку из `location` отдаём клиенту.
///   Webhook: FreeKassa POST'ит form-urlencoded {MERCHANT_ID, AMOUNT,
///   MERCHANT_ORDER_ID, SIGN, intid, P_EMAIL, CUR_ID}. Уведомление приходит
///   ТОЛЬКО по успешной оплате. Проверяем SIGN, отвечаем ровно "YES".
///
/// Замечание про `i`: 44 = СБП/QR, 36 = карты РФ (подтверждены мерчантом).
/// Код для SberPay не подтверждён — при method=SberPay `i` не отправляем,
/// FreeKassa покажет свою страницу выбора со СберПеем. Как только придёт
/// код — добавляем "i_sberpay" в Settings, редеплой не нужен.
/// </summary>
public class FreeKassaPaymentProvider : IPaymentProvider
{
    private const string DefaultBaseUrl = "https://api.fk.life/v1";
    private const string HttpClientName = "freekassa";

    // Server public IP — fallback for FreeKassa's "ip обязателен, 127.0.0.1
    // блокируется" rule when a guest checkout has no usable client IP.
    private const string DefaultFallbackIp = "145.223.90.75";

    // FreeKassa notification source IPs (docs §1.4). Soft-checked (log only)
    // because behind nginx/docker RemoteIpAddress is the proxy — the md5 SIGN
    // is the real gate.
    private static readonly HashSet<string> FreeKassaWebhookIps = new()
    {
        "168.119.157.136", "168.119.60.227", "178.154.197.79", "51.250.54.238",
    };

    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PaymentAnonymizer _anonymizer;
    private readonly ILogger<FreeKassaPaymentProvider> _log;

    public FreeKassaPaymentProvider(ApplicationDbContext db, IHttpClientFactory httpFactory,
        PaymentAnonymizer anonymizer, ILogger<FreeKassaPaymentProvider> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _anonymizer = anonymizer;
        _log = log;
    }

    public string Name => "freekassa";
    public string DisplayName => "FreeKassa";

    public IReadOnlyList<PaymentMethod> SupportedMethods => new[]
    {
        PaymentMethod.Card,
        PaymentMethod.SBP,
        PaymentMethod.SberPay,
    };

    public bool IsEnabled
    {
        get
        {
            var cfg = _db.PaymentProviderConfigs.AsNoTracking()
                .FirstOrDefault(c => c.Name == Name);
            return cfg?.IsEnabled == true
                && !string.IsNullOrEmpty(cfg.MerchantId)  // shopId
                && !string.IsNullOrEmpty(cfg.ApiKey)      // API key (request sig)
                && !string.IsNullOrEmpty(cfg.SecretKey);  // secret word 2 (webhook sig)
        }
    }

    public bool SupportsRefund => false; // FreeKassa refunds are done in LK, not API.

    // ════════════════════════════════════════════════════════════════════
    // Create payment
    // ════════════════════════════════════════════════════════════════════

    public async Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null) return PaymentResult.Failed("FreeKassa не настроена в админке.");
        if (!cfg.IsEnabled) return PaymentResult.Failed("FreeKassa временно отключена.");
        if (string.IsNullOrEmpty(cfg.MerchantId))
            return PaymentResult.Failed("FreeKassa: не задан shopId (MerchantId).");
        if (string.IsNullOrEmpty(cfg.ApiKey))
            return PaymentResult.Failed("FreeKassa: не задан API-ключ (ApiKey).");

        if (!int.TryParse(cfg.MerchantId, out var shopId))
            return PaymentResult.Failed($"FreeKassa: shopId '{cfg.MerchantId}' не число.");

        // Our internal txn-id — sent as `paymentId`, echoed back in the webhook
        // as MERCHANT_ORDER_ID, so it matches PaymentTransaction.TransactionId.
        var ourTransactionId = TxnIdHelper.Generate(maxLength: 32);

        // Anonymise for whitelisted internal-test accounts; real customers pass
        // through. FreeKassa requires a real-looking email + routable IP.
        var contacts = _anonymizer.Anonymize(request.Email, request.Phone, request.ClientIp);

        var email = !string.IsNullOrWhiteSpace(contacts.Email)
            ? contacts.Email
            : $"{ourTransactionId}@telegram.org";   // docs allow тгid@telegram.org

        var ip = NormaliseIp(contacts.Ip, ReadSetting(cfg, "fallbackIp") ?? DefaultFallbackIp);

        var amountStr = FormatAmount(request.Amount);
        var currency  = string.IsNullOrEmpty(request.Currency) ? "RUB" : request.Currency;
        var nonce     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); // strictly increasing

        // Build the signed parameter set. Everything EXCEPT `signature`. `i` is
        // optional (omitted → FreeKassa shows its own method-selection page).
        var p = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["amount"]    = amountStr,
            ["currency"]  = currency,
            ["email"]     = email,
            ["ip"]        = ip,
            ["nonce"]     = nonce.ToString(CultureInfo.InvariantCulture),
            ["paymentId"] = ourTransactionId,
            ["shopId"]    = shopId.ToString(CultureInfo.InvariantCulture),
        };

        var iCode = ResolveMethodCode(cfg, request.Method);
        if (iCode.HasValue)
            p["i"] = iCode.Value.ToString(CultureInfo.InvariantCulture);

        // signature = HMAC-SHA256(apiKey, values sorted-by-key joined with '|')
        var signature = HmacSha256Hex(cfg.ApiKey!, string.Join("|", p.Values));

        // JSON body: numeric fields as numbers (their canonical ToString matches
        // the signed string exactly — no BillionPay-style trailing-zero drift),
        // strings as strings.
        var body = new Dictionary<string, object?>
        {
            ["shopId"]    = shopId,
            ["nonce"]     = nonce,
            ["paymentId"] = ourTransactionId,
            ["amount"]    = decimal.Parse(amountStr, CultureInfo.InvariantCulture), // scale-minimal → "100"/"100.5"
            ["currency"]  = currency,
            ["email"]     = email,
            ["ip"]        = ip,
            ["signature"] = signature,
        };
        if (iCode.HasValue) body["i"] = iCode.Value;

        var url  = ReadBaseUrl(cfg) + "/orders/create";
        var json = JsonSerializer.Serialize(body);

        try
        {
            _log.LogInformation(
                "FreeKassa → POST {Url} txn={Txn} amount={Amt} i={I} body={Body}",
                url, ourTransactionId, amountStr, iCode, json);

            using var http = _httpFactory.CreateClient(HttpClientName);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url, content, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            _log.LogInformation("FreeKassa ← {Status} {Body}", (int)resp.StatusCode, raw);

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var tEl) ? tEl.GetString() : null;
            if (!string.Equals(type, "success", StringComparison.OrdinalIgnoreCase))
            {
                // FreeKassa error shape: { type:"error", message:"..." }
                var msg = root.TryGetProperty("message", out var mEl) ? mEl.GetString() : raw;
                return PaymentResult.Failed($"FreeKassa: {msg}", ((int)resp.StatusCode).ToString());
            }

            var location = root.TryGetProperty("location", out var lEl) ? lEl.GetString() : null;
            var fkOrderId = root.TryGetProperty("orderId", out var oEl)
                ? (oEl.ValueKind == JsonValueKind.Number ? oEl.GetInt64().ToString() : oEl.GetString())
                : null;

            if (string.IsNullOrEmpty(location))
                return PaymentResult.Failed($"FreeKassa не вернула location (ссылку на оплату): {raw}");

            _log.LogInformation(
                "FreeKassa → order created txn={Txn} fk_order={Fk} location={Loc}",
                ourTransactionId, fkOrderId, location);

            return new PaymentResult
            {
                Success = true,
                TransactionId = ourTransactionId,
                ProviderTransactionId = fkOrderId,
                RedirectUrl = location,
                Status = PaymentStatus.Pending,
                ProviderData = new Dictionary<string, string>
                {
                    ["freekassa_order_id"] = fkOrderId ?? "",
                    ["redirect_url"] = location,
                },
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "FreeKassa /orders/create failed");
            return PaymentResult.Failed($"FreeKassa недоступна: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Webhook validation
    // ════════════════════════════════════════════════════════════════════

    public async Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers, string body, CancellationToken ct = default)
    {
        var cfg = await LoadConfigAsync(ct);
        if (cfg == null || string.IsNullOrEmpty(cfg.MerchantId) || string.IsNullOrEmpty(cfg.SecretKey))
            return WebhookValidationResult.Invalid("FreeKassa not configured.");

        if (string.IsNullOrWhiteSpace(body))
            return WebhookValidationResult.Invalid("Empty body.");

        // Body is the reconstructed form: MERCHANT_ID=..&AMOUNT=..&SIGN=..&…
        var f = ParseForm(body);

        var merchantId = Get(f, "MERCHANT_ID");
        var amount     = Get(f, "AMOUNT");
        var orderId    = Get(f, "MERCHANT_ORDER_ID");
        var sign       = Get(f, "SIGN");

        if (string.IsNullOrEmpty(orderId))
            return WebhookValidationResult.Invalid("FreeKassa webhook: no MERCHANT_ORDER_ID.");
        if (!string.Equals(merchantId, cfg.MerchantId, StringComparison.Ordinal))
            return WebhookValidationResult.Invalid(
                $"FreeKassa webhook: MERCHANT_ID mismatch (got '{merchantId}').");

        // Signature: md5(MERCHANT_ID:AMOUNT:SECRET_WORD_2:MERCHANT_ORDER_ID)
        var expected = Md5Hex($"{merchantId}:{amount}:{cfg.SecretKey}:{orderId}");
        if (!string.Equals(expected, sign, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning(
                "FreeKassa webhook: SIGN mismatch for order {Order} (got '{Got}')", orderId, sign);
            return WebhookValidationResult.Invalid("FreeKassa webhook: bad SIGN.");
        }

        // Soft IP check — behind a proxy RemoteIpAddress won't be FreeKassa's,
        // so only log, never reject (md5 SIGN already proves authenticity).
        if (headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrEmpty(xff))
        {
            var src = xff.Split(',')[0].Trim();
            if (!FreeKassaWebhookIps.Contains(src))
                _log.LogInformation("FreeKassa webhook from non-listed IP {Ip} (SIGN valid, accepting)", src);
        }

        decimal amt = 0;
        decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out amt);

        // FreeKassa only fires the notification on a SUCCESSFUL payment.
        return new WebhookValidationResult
        {
            IsValid       = true,
            TransactionId = orderId,
            NewStatus     = PaymentStatus.Completed,
            Amount        = amt,
            RawData       = body,
            ResponseBody  = "YES", // FreeKassa requires the literal "YES" back.
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Status polling (FreeKassa API v1 orders list) — optional, best-effort
    // ════════════════════════════════════════════════════════════════════

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId,
        CancellationToken ct = default)
    {
        // FreeKassa's /orders endpoint needs a signed nonce query too; the
        // webhook is authoritative and fires reliably, so we don't poll.
        // Returning Pending keeps the sweeper from flipping anything.
        return Task.FromResult(new PaymentStatusResult
        {
            TransactionId = transactionId,
            Status        = PaymentStatus.Pending,
            Message       = "FreeKassa: статус подтверждается вебхуком.",
            UpdatedAt     = DateTime.UtcNow,
        });
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(PaymentResult.Failed(
            "Возврат FreeKassa делается в личном кабинете, не через API."));
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private async Task<PaymentProviderConfig?> LoadConfigAsync(CancellationToken ct)
        => await _db.PaymentProviderConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name == Name, ct);

    /// <summary>Maps our method to FreeKassa's `i`. Null → omit `i`.</summary>
    private int? ResolveMethodCode(PaymentProviderConfig cfg, PaymentMethod method)
    {
        // Defaults confirmed by merchant: 44 = СБП/QR, 36 = карты РФ.
        int? iCard    = ReadIntSetting(cfg, "i_card")    ?? 36;
        int? iSbp     = ReadIntSetting(cfg, "i_sbp")     ?? 44;
        int? iSberPay = ReadIntSetting(cfg, "i_sberpay"); // unknown → null → omit

        return method switch
        {
            PaymentMethod.Card    => iCard,
            PaymentMethod.SBP     => iSbp,
            PaymentMethod.SberPay => iSberPay,
            _                     => iCard,
        };
    }

    /// <summary>FreeKassa blocks 127.0.0.1 / empty — fall back to server IP.</summary>
    private static string NormaliseIp(string? ip, string fallback)
    {
        if (string.IsNullOrWhiteSpace(ip)) return fallback;
        var t = ip.Trim();
        if (t is "127.0.0.1" or "::1" or "0.0.0.0" || t.StartsWith("127.")) return fallback;
        return t;
    }

    /// <summary>Amount without trailing zeros: "100" / "100.5" — signed string
    /// and JSON number stay byte-identical (avoids the BillionPay 53.00 trap).</summary>
    private static string FormatAmount(decimal amount)
        => amount.ToString("0.####", CultureInfo.InvariantCulture);

    private static string HmacSha256Hex(string key, string message)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Md5Hex(string s)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) { d[Uri.UnescapeDataString(pair)] = ""; continue; }
            var k = Uri.UnescapeDataString(pair[..eq]);
            var v = Uri.UnescapeDataString(pair[(eq + 1)..]);
            d[k] = v;
        }
        return d;
    }

    private static string Get(Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) ? v : "";

    private static string ReadBaseUrl(PaymentProviderConfig cfg)
        => ReadSetting(cfg, "baseUrl")?.TrimEnd('/') ?? DefaultBaseUrl;

    private static string? ReadSetting(PaymentProviderConfig cfg, string key)
    {
        if (string.IsNullOrWhiteSpace(cfg.Settings)) return null;
        try
        {
            using var doc = JsonDocument.Parse(cfg.Settings);
            if (doc.RootElement.TryGetProperty(key, out var el))
            {
                if (el.ValueKind == JsonValueKind.String) return el.GetString();
                if (el.ValueKind is JsonValueKind.Number) return el.ToString();
            }
        }
        catch { /* malformed Settings — ignore */ }
        return null;
    }

    private static int? ReadIntSetting(PaymentProviderConfig cfg, string key)
    {
        var v = ReadSetting(cfg, key);
        return int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
