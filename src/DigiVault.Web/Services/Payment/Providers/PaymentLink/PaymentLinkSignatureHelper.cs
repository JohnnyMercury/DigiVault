using System.Security.Cryptography;
using System.Text;

namespace DigiVault.Web.Services.Payment.Providers.PaymentLink;

/// <summary>
/// Builds and verifies PaymentLink merchant-interface signatures, exactly as
/// documented at start.paymentlnk.com (Merchant Interface v3.0).
///
/// PaymentLink uses *different* canonical strings depending on direction:
///
///   • Outgoing  (POST /api/payment/start, our → PL):
///       amount:amountcurr:currency:number:description:trtype:account
///       [:paytoken][:backURL][:cf1:cf2:cf3]:k1:k2
///
///   • Incoming  (PL → us, "Payment Confirmation Request URL", opertype=pay):
///       opertype:amount:amountcurr:currency:number:description:trtype:account
///       [:cf1:cf2:cf3][:paytoken][:backURL]:transID:datetime:k1:k2
///
/// Optional fields (paytoken / backURL) are dropped — both the value AND the
/// trailing colon — when empty/null. The cf1/cf2/cf3 trio is included as a
/// group: if any one is set, all three appear; if all three are empty, the
/// entire trio is dropped.
///
/// Hash is one of:
///   1. md5  — uppercase hex of MD5 over the canonical string.
///   2. hmac_sha256 — HMAC-SHA256 with key = secret_key_1 ‖ secret_key_2 (no
///      separator), output as lowercase hex (PaymentLink compares
///      case-insensitively, but the PHP example uses md5 + strtoupper).
/// Algorithm is configurable in the merchant LK; we mirror it via
/// <c>{ "algo": "md5" | "hmac_sha256" }</c> in PaymentProviderConfig.Settings.
/// </summary>
public static class PaymentLinkSignatureHelper
{
    /// <summary>
    /// Outgoing signature for POST /api/payment/start. Used when we redirect
    /// the customer to PaymentLink's hosted checkout.
    /// </summary>
    public static string Build(
        decimal amount,
        string amountCurr,
        string currency,
        string number,
        string description,
        int trtype,
        string account,
        string? paytoken,
        string? backUrl,
        string? cf1,
        string? cf2,
        string? cf3,
        string secretKey1,
        string secretKey2,
        string algo /* "hmac_sha256" or "md5" */)
    {
        var sb = new StringBuilder();
        sb.Append(amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(':').Append(amountCurr);
        sb.Append(':').Append(currency);
        sb.Append(':').Append(number);
        sb.Append(':').Append(description);
        sb.Append(':').Append(trtype);
        sb.Append(':').Append(account);

        // Optional - present only when non-empty (per spec § 4.1).
        if (!string.IsNullOrEmpty(paytoken)) sb.Append(':').Append(paytoken);
        if (!string.IsNullOrEmpty(backUrl))  sb.Append(':').Append(backUrl);

        // cf1/cf2/cf3 are included as a group: if any is set, all three must
        // appear (empty strings keep the colon). If all three are empty, the
        // entire trio is dropped.
        var anyCf = !string.IsNullOrEmpty(cf1) || !string.IsNullOrEmpty(cf2) || !string.IsNullOrEmpty(cf3);
        if (anyCf)
        {
            sb.Append(':').Append(cf1 ?? "");
            sb.Append(':').Append(cf2 ?? "");
            sb.Append(':').Append(cf3 ?? "");
        }

        return Hash(sb.ToString(), secretKey1, secretKey2, algo);
    }

    /// <summary>
    /// Verifies the confirmation webhook signature (PL → us, opertype=pay).
    /// PaymentLink POSTs this BEFORE charging the card, asking the merchant
    /// to approve. The signature canonical string is opertype-led and ends
    /// with transID:datetime, NOT the same as the outgoing signature.
    /// </summary>
    public static bool VerifyConfirmationWebhook(
        IDictionary<string, string> form,
        string secretKey1,
        string secretKey2,
        string algo)
    {
        string Get(string k) => form.TryGetValue(k, out var v) ? v : "";

        if (!form.TryGetValue("signature", out var received) || string.IsNullOrEmpty(received))
            return false;

        var amount      = Get("amount");
        var amountCurr  = Get("amountcurr");
        var currency    = Get("currency");
        var number      = Get("number");
        var description = Get("description");
        var trtype      = Get("trtype");
        var account     = Get("account");
        var paytoken    = Get("paytoken");
        var backUrl     = Get("backURL");
        var cf1         = Get("cf1");
        var cf2         = Get("cf2");
        var cf3         = Get("cf3");
        var transId     = Get("transID");
        var datetime    = Get("datetime");
        var opertype    = Get("opertype");

        // opertype:amount:amountcurr:currency:number:description:trtype:account
        // [:cf1:cf2:cf3][:paytoken][:backURL]:transID:datetime
        var sb = new StringBuilder();
        sb.Append(opertype);
        sb.Append(':').Append(amount);
        sb.Append(':').Append(amountCurr);
        sb.Append(':').Append(currency);
        sb.Append(':').Append(number);
        sb.Append(':').Append(description);
        sb.Append(':').Append(trtype);
        sb.Append(':').Append(account);

        var anyCf = !string.IsNullOrEmpty(cf1) || !string.IsNullOrEmpty(cf2) || !string.IsNullOrEmpty(cf3);
        if (anyCf)
        {
            sb.Append(':').Append(cf1);
            sb.Append(':').Append(cf2);
            sb.Append(':').Append(cf3);
        }
        if (!string.IsNullOrEmpty(paytoken)) sb.Append(':').Append(paytoken);
        if (!string.IsNullOrEmpty(backUrl))  sb.Append(':').Append(backUrl);

        sb.Append(':').Append(transId);
        sb.Append(':').Append(datetime);

        var expected = Hash(sb.ToString(), secretKey1, secretKey2, algo);
        return string.Equals(expected, received, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string data, string secretKey1, string secretKey2, string algo)
    {
        if (string.Equals(algo, "md5", StringComparison.OrdinalIgnoreCase))
        {
            // PHP example: md5("<canonical>:<key1>:<key2>"), uppercase hex.
            // The keys participate as plain colon-separated tail in the input.
            var withKeys = data + ":" + (secretKey1 ?? "") + ":" + (secretKey2 ?? "");
            var md5 = MD5.HashData(Encoding.UTF8.GetBytes(withKeys));
            return Convert.ToHexString(md5).ToUpperInvariant();
        }

        // HMAC-SHA256 with key = secret_key_1 || secret_key_2 (no separator
        // between keys, per spec § 4.1: «сцеплённые без дополнительных
        // промежуточных символов ключи»). The canonical <data> is hashed as-is;
        // keys are NOT appended. Output as uppercase hex; PaymentLink compares
        // case-insensitively.
        var key = Encoding.UTF8.GetBytes((secretKey1 ?? "") + (secretKey2 ?? ""));
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToUpperInvariant();
    }
}
