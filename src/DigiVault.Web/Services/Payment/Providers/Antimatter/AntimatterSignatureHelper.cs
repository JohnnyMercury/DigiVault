using System.Security.Cryptography;
using System.Text;

namespace DigiVault.Web.Services.Payment.Providers.Antimatter;

/// <summary>
/// Webhook signature for Antimatter Gateway (antimatter.profit-gateway.com).
/// Per their docs: sign = md5(paymentId + timestamp + apiKey), lowercase hex.
/// </summary>
public static class AntimatterSignatureHelper
{
    public static string BuildSignature(string paymentId, string timestamp, string apiKey)
    {
        var data = $"{paymentId}{timestamp}{apiKey}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool FixedTimeEquals(string? actualSignature, string expectedSignature)
    {
        if (string.IsNullOrWhiteSpace(actualSignature) || string.IsNullOrWhiteSpace(expectedSignature))
            return false;

        var actual = Encoding.ASCII.GetBytes(actualSignature.Trim().ToLowerInvariant());
        var expected = Encoding.ASCII.GetBytes(expectedSignature.Trim().ToLowerInvariant());

        return actual.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
