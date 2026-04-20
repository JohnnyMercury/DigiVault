namespace DigiVault.Core.Entities;

/// <summary>
/// User review for a product. Polymorphic FK (Game / GiftCard / VpnProvider) same as GameProduct.
/// UserId is nullable so we can seed demo reviews that aren't tied to a real account.
/// </summary>
public class ProductReview
{
    public int Id { get; set; }

    // Optional link to real user (null for seeded demo reviews)
    public string? UserId { get; set; }
    public virtual ApplicationUser? User { get; set; }

    // Display fields (always filled — whether from real user or demo seed)
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorRole { get; set; } // e.g. "Постоянный клиент", "Игрок в Fortnite", "Пользуюсь с 2024 г."

    // Polymorphic product reference — exactly one of these should be set
    public int? GameId { get; set; }
    public virtual Game? Game { get; set; }

    public int? GiftCardId { get; set; }
    public virtual GiftCard? GiftCard { get; set; }

    public int? VpnProviderId { get; set; }
    public virtual VpnProvider? VpnProvider { get; set; }

    // Review content
    public int Rating { get; set; } // 1-5
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    public int HelpfulCount { get; set; } = 0;
    public bool IsVerifiedPurchase { get; set; } = true;
    public bool IsApproved { get; set; } = true;

    // Optional admin reply (from "Команда Key Zona")
    public string? AdminReply { get; set; }
    public DateTime? AdminReplyAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Convenience getters — used in views
    public string ProductDisplay
    {
        get
        {
            if (Game != null) return Game.Name;
            if (GiftCard != null) return GiftCard.Name;
            if (VpnProvider != null) return VpnProvider.Name;
            return "";
        }
    }

    public string ProductSlug
    {
        get
        {
            if (Game != null) return Game.Slug;
            if (GiftCard != null) return GiftCard.Slug;
            if (VpnProvider != null) return VpnProvider.Slug;
            return "";
        }
    }

    public string ProductCategory
    {
        get
        {
            if (GameId != null) return "games";
            if (GiftCardId != null) return "giftcards";
            if (VpnProviderId != null) return "vpn";
            return "";
        }
    }
}
