using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigiVault.Web.Services.Fulfilment;

/// <summary>
/// Polymorphic JSON payload describing the actual delivered credential for an
/// <see cref="DigiVault.Core.Entities.OrderItem"/>. Two variants:
///   - <see cref="CodeCredential"/> - activation code (gift cards, PSN/Xbox/Nintendo, VPN)
///   - <see cref="ConfirmationCredential"/> - top-up receipt (game currency, Telegram Premium)
///
/// Discriminator field <c>kind</c> is "code" or "confirmation".
/// Stored as <c>jsonb</c> in <see cref="DigiVault.Core.Entities.OrderItem.DeliveryPayloadJson"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CodeCredential), "code")]
[JsonDerivedType(typeof(ConfirmationCredential), "confirmation")]
[JsonDerivedType(typeof(ContactSupportCredential), "support")]
public abstract class DeliveryPayload
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // CRITICAL: serialise via the abstract base type so System.Text.Json
    // emits the `kind` discriminator from [JsonPolymorphic]. Using
    // `Serialize(this, GetType(), ...)` writes the concrete subtype directly
    // (no discriminator), and Deserialize<DeliveryPayload> later returns null
    // - which is what made every order render the «no payload» fallback
    // banner instead of the real Code / Confirmation / Support credential.
    public string Serialize() => JsonSerializer.Serialize<DeliveryPayload>(this, JsonOptions);

    public static DeliveryPayload? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<DeliveryPayload>(json, JsonOptions);
            if (payload != null) return payload;
        }
        catch { /* fall through to legacy sniffing */ }

        // Backwards-compat: orders sealed before the Serialize fix above wrote
        // the JSON without a `kind` field. Try to sniff the variant by the
        // shape of the document so old orders still render correctly.
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out _))
                return JsonSerializer.Deserialize<CodeCredential>(json, JsonOptions);
            if (root.TryGetProperty("supportUsername", out _))
                return JsonSerializer.Deserialize<ContactSupportCredential>(json, JsonOptions);
            if (root.TryGetProperty("transactionId", out _))
                return JsonSerializer.Deserialize<ConfirmationCredential>(json, JsonOptions);
        }
        catch { }
        return null;
    }
}

/// <summary>
/// Single-use activation code (Netflix, Spotify, Apple, YouTube, PSN, Xbox,
/// Nintendo eShop, NordVPN, ExpressVPN, Surfshark…). The user copies the code
/// and redeems it on the issuer's platform.
/// </summary>
public class CodeCredential : DeliveryPayload
{
    public string Code { get; set; } = "";

    /// <summary>"USA" / "EU" / "TR" / "RU" - where the code is valid.</summary>
    public string? Region { get; set; }

    /// <summary>Where to redeem the code (free-text instructions).</summary>
    public string? Instructions { get; set; }

    /// <summary>Optional code expiration date (ISO date or null).</summary>
    public string? ExpiresAt { get; set; }
}

/// <summary>
/// Order requires manual processing. Payment went through, but the actual
/// product (Steam Wallet top-up, in-game currency, VPN access, Telegram
/// Premium activation) is fulfilled by an operator on the support side after
/// the customer reaches out. Renders as a «security check» banner with a
/// Telegram contact button on the order page and in the email receipt.
/// </summary>
public class ContactSupportCredential : DeliveryPayload
{
    /// <summary>Headline shown to the customer (e.g. «Платёж на проверке безопасности»).</summary>
    public string Title { get; set; } = "Платёж на проверке Системой безопасности";

    /// <summary>Long-form explanation, can include product-specific notes.</summary>
    public string Message { get; set; } =
        "Ваш платёж попал на проверку Системой безопасности. Пожалуйста, свяжитесь с нами в Telegram - менеджер закроет заказ за пару минут.";

    /// <summary>Telegram username without the leading «@» (e.g. <c>digivault_support</c>).</summary>
    public string SupportUsername { get; set; } = "digivault_support";

    /// <summary>Reference number the customer should mention to the operator.</summary>
    public string OrderRef { get; set; } = "";

    /// <summary>UTC timestamp when delivery was logged.</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>Optional recipient identifier (Steam login, email, UID …) - handy for the operator.</summary>
    public string? Recipient { get; set; }

    /// <summary>What was paid for (Steam Wallet 1000₽, V-Bucks 13500, Surfshark 12 мес.).</summary>
    public string? ProductName { get; set; }
}

/// <summary>
/// Receipt-style payload - the actual product (in-game currency, Telegram
/// Premium subscription, etc.) was delivered out-of-band to the recipient's
/// account. There is no code to redeem; the customer just sees confirmation.
/// </summary>
public class ConfirmationCredential : DeliveryPayload
{
    /// <summary>Recipient identifier - username, UID, email, etc. (from Order.DeliveryInfo).</summary>
    public string Recipient { get; set; } = "";

    /// <summary>Human-readable amount: "1000 V-Bucks", "Premium 6 месяцев", etc.</summary>
    public string Amount { get; set; } = "";

    /// <summary>Internal transaction id we show to the customer for support reference.</summary>
    public string TransactionId { get; set; } = "";

    /// <summary>UTC timestamp when delivery was completed.</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>Optional subscription expiry (ISO date or null) - for time-limited products.</summary>
    public string? ValidUntil { get; set; }
}
