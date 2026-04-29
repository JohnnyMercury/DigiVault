using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigiVault.Web.Services.Fulfilment;

/// <summary>
/// Polymorphic JSON payload describing the actual delivered credential for an
/// <see cref="DigiVault.Core.Entities.OrderItem"/>. Two variants:
///   - <see cref="CodeCredential"/> — activation code (gift cards, PSN/Xbox/Nintendo, VPN)
///   - <see cref="ConfirmationCredential"/> — top-up receipt (game currency, Telegram Premium)
///
/// Discriminator field <c>kind</c> is "code" or "confirmation".
/// Stored as <c>jsonb</c> in <see cref="DigiVault.Core.Entities.OrderItem.DeliveryPayloadJson"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CodeCredential), "code")]
[JsonDerivedType(typeof(ConfirmationCredential), "confirmation")]
public abstract class DeliveryPayload
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Serialize() => JsonSerializer.Serialize(this, GetType(), JsonOptions);

    public static DeliveryPayload? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<DeliveryPayload>(json, JsonOptions); }
        catch { return null; }
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

    /// <summary>"USA" / "EU" / "TR" / "RU" — where the code is valid.</summary>
    public string? Region { get; set; }

    /// <summary>Where to redeem the code (free-text instructions).</summary>
    public string? Instructions { get; set; }

    /// <summary>Optional code expiration date (ISO date or null).</summary>
    public string? ExpiresAt { get; set; }
}

/// <summary>
/// Receipt-style payload — the actual product (in-game currency, Telegram
/// Premium subscription, etc.) was delivered out-of-band to the recipient's
/// account. There is no code to redeem; the customer just sees confirmation.
/// </summary>
public class ConfirmationCredential : DeliveryPayload
{
    /// <summary>Recipient identifier — username, UID, email, etc. (from Order.DeliveryInfo).</summary>
    public string Recipient { get; set; } = "";

    /// <summary>Human-readable amount: "1000 V-Bucks", "Premium 6 месяцев", etc.</summary>
    public string Amount { get; set; } = "";

    /// <summary>Internal transaction id we show to the customer for support reference.</summary>
    public string TransactionId { get; set; } = "";

    /// <summary>UTC timestamp when delivery was completed.</summary>
    public DateTime CompletedAt { get; set; }

    /// <summary>Optional subscription expiry (ISO date or null) — for time-limited products.</summary>
    public string? ValidUntil { get; set; }
}
