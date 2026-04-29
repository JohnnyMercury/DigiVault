using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigiVault.Web.Services.Payment.Providers.Enot;

/// <summary>
/// Computes the Enot webhook signature exactly the way their docs specify
/// (matches the reference Python:
///
///   <c>json.dumps(hook_body, sort_keys=True, separators=(', ', ': '))</c>
///
/// then <c>hmac-sha256</c> with the merchant's "Дополнительный ключ").
///
/// .NET's built-in <see cref="JsonSerializer"/> can't reproduce this
/// byte-for-byte (different separators, doesn't sort keys recursively),
/// so we walk the parsed tree manually and emit Python-compatible JSON.
/// </summary>
public static class EnotSignatureHelper
{
    /// <summary>
    /// True when the given raw webhook body matches the supplied
    /// <c>x-api-sha256-signature</c> header for the configured additional key.
    /// </summary>
    public static bool Verify(string rawBody, string headerSignature, string additionalKey)
    {
        if (string.IsNullOrEmpty(headerSignature) || string.IsNullOrEmpty(additionalKey))
            return false;

        var expected = Compute(rawBody, additionalKey);
        // Constant-time compare.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(headerSignature.Trim().ToLowerInvariant()));
    }

    /// <summary>
    /// Build the canonical Python-style JSON string for an arbitrary
    /// <see cref="JsonElement"/> tree, then HMAC-SHA256 it with
    /// <paramref name="additionalKey"/> as the secret. Hex-lowercase.
    /// </summary>
    public static string Compute(string rawBody, string additionalKey)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var sb = new StringBuilder();
        WriteCanonical(doc.RootElement, sb);
        var canonical = sb.ToString();

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(additionalKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Exposed for unit/integration tests — returns the canonical string we hash.</summary>
    public static string CanonicalJson(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var sb = new StringBuilder();
        WriteCanonical(doc.RootElement, sb);
        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────────
    // Python-style canonical writer
    //   Object:  { "key": value, "key": value }      (alpha-sorted keys)
    //   Array :  [ item, item, item ]
    //   String:  "..."  — escapes only " and \, leaves / and unicode raw
    //   Number:  as-is from the original JSON (preserves "100.00" etc.)
    //   Bool / Null: true / false / null
    // ────────────────────────────────────────────────────────────────────

    private static void WriteCanonical(JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                sb.Append('{');
                bool first = true;
                foreach (var prop in el.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    WriteString(prop.Name, sb);
                    sb.Append(": ");
                    WriteCanonical(prop.Value, sb);
                }
                sb.Append('}');
                break;

            case JsonValueKind.Array:
                sb.Append('[');
                bool firstItem = true;
                foreach (var item in el.EnumerateArray())
                {
                    if (!firstItem) sb.Append(", ");
                    firstItem = false;
                    WriteCanonical(item, sb);
                }
                sb.Append(']');
                break;

            case JsonValueKind.String:
                WriteString(el.GetString() ?? "", sb);
                break;

            case JsonValueKind.Number:
                // Preserve the original textual representation. Python writes
                // ints without decimal point, floats with full precision.
                // GetRawText() gives us exactly what came in over the wire.
                sb.Append(el.GetRawText());
                break;

            case JsonValueKind.True:  sb.Append("true");  break;
            case JsonValueKind.False: sb.Append("false"); break;
            case JsonValueKind.Null:  sb.Append("null");  break;

            default:
                throw new InvalidOperationException($"Unexpected JSON kind: {el.ValueKind}");
        }
    }

    private static void WriteString(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b");  break;
                case '\f': sb.Append("\\f");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    // No escape for /, no \uXXXX for non-ASCII —
                    // matches Python's json.dumps(ensure_ascii=False).
                    if (ch < 0x20)
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}
