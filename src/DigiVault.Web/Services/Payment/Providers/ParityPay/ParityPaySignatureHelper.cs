using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigiVault.Web.Services.Payment.Providers.ParityPay;

public static class ParityPaySignatureHelper
{
    public static string BuildSignature(IEnumerable<KeyValuePair<string, object?>> parameters, string secretKey)
    {
        var concatenated = string.Concat(parameters
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => ToCanonicalValue(kv.Value)));

        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secretKey),
            Encoding.UTF8.GetBytes(concatenated));

        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    public static string BuildSignature(JsonElement root, string secretKey)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("ParityPay signature payload must be a JSON object.", nameof(root));

        return BuildSignature(
            root.EnumerateObject()
                .Select(p => new KeyValuePair<string, object?>(p.Name, p.Value)),
            secretKey);
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

    private static string ToCanonicalValue(object? value)
    {
        return value switch
        {
            null => "",
            JsonElement element => ToCanonicalJsonValue(element),
            string s => s,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? "",
        };
    }

    private static string ToCanonicalJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => JsonSerializer.Serialize(element),
        };
    }
}
