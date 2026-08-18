using System.Security.Cryptography;
using System.Text;

namespace DigiVault.Web.Services.Payment.Providers.RollyPay;

/// <summary>
/// Webhook signature verification for RollyPay.
///
/// Per docs (docs.rollypay.io/api/callbacks): the callback carries
///   X-Signature  - HMAC-SHA256 hex digest
///   X-Timestamp  - unix timestamp of sending
/// and the signed string is  timestamp + "." + rawBody , keyed with the
/// terminal's signing_secret.
///
/// The raw body matters: it must be the exact bytes RollyPay sent, not a
/// re-serialized object. WebhooksController reads Request.Body verbatim for
/// JSON content types, which is what we rely on here.
/// </summary>
internal static class RollyPaySignatureHelper
{
    /// <summary>
    /// Build the expected signature: hex(HMAC-SHA256(signingSecret, timestamp + "." + body)).
    /// </summary>
    public static string BuildSignature(string timestamp, string body, string signingSecret)
    {
        var payload = $"{timestamp}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time comparison (case-insensitive hex) so a mismatch cannot be
    /// probed byte-by-byte via response timing.
    /// </summary>
    public static bool FixedTimeEquals(string? received, string expected)
    {
        if (string.IsNullOrWhiteSpace(received)) return false;

        var a = Encoding.UTF8.GetBytes(received.Trim().ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(expected.Trim().ToLowerInvariant());

        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
