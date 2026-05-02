using System.Security.Cryptography;
using System.Text;

namespace DigiVault.Web.Services.Payment.Providers.PaymentLink;

/// <summary>
/// Builds the PaymentLink merchant-interface signature exactly as documented at
/// start.paymentlnk.com (v3.0):
///
///   string = amount:amountcurr:currency:number:description:trtype:account[:paytoken][:backURL][:cf1:cf2:cf3]
///
/// Optional fields (paytoken, backURL, cf1/cf2/cf3) are skipped — both the
/// value AND the trailing colon — when empty/null. Either MD5 or HMAC-SHA256
/// can be configured per merchant in the LK; we default to HMAC-SHA256 with
/// the key formed by concatenating secret_key_1 + secret_key_2 (no separator).
/// </summary>
public static class PaymentLinkSignatureHelper
{
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

        var data = sb.ToString();

        if (string.Equals(algo, "md5", StringComparison.OrdinalIgnoreCase))
        {
            var md5 = MD5.HashData(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(md5).ToLowerInvariant();
        }

        // Default: HMAC-SHA256 with key = secret_key_1 || secret_key_2
        var key = Encoding.UTF8.GetBytes((secretKey1 ?? "") + (secretKey2 ?? ""));
        var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies a webhook signature received from PaymentLink. The provider
    /// posts the same form fields back to our hook URL, and the signature is
    /// computed over the same canonical string as on the way out.
    /// </summary>
    public static bool Verify(
        IDictionary<string, string> form,
        string secretKey1,
        string secretKey2,
        string algo)
    {
        string Get(string key) => form.TryGetValue(key, out var v) ? v : "";

        if (!form.TryGetValue("signature", out var received) || string.IsNullOrEmpty(received))
            return false;

        if (!decimal.TryParse(Get("amount"),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var amt))
            return false;

        var expected = Build(
            amount:      amt,
            amountCurr:  Get("amountcurr"),
            currency:    Get("currency"),
            number:      Get("number"),
            description: Get("description"),
            trtype:      int.TryParse(Get("trtype"), out var t) ? t : 1,
            account:     Get("account"),
            paytoken:    Get("paytoken"),
            backUrl:     Get("backURL"),
            cf1:         Get("cf1"),
            cf2:         Get("cf2"),
            cf3:         Get("cf3"),
            secretKey1:  secretKey1,
            secretKey2:  secretKey2,
            algo:        algo);

        return string.Equals(expected, received, StringComparison.OrdinalIgnoreCase);
    }
}
