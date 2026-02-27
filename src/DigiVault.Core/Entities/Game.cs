namespace DigiVault.Core.Entities;

/// <summary>
/// Represents a game (Fortnite, Roblox, etc.)
/// </summary>
public class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty; // fortnite, roblox, pubg, etc.
    public string? Subtitle { get; set; } // "Bang Bang" for Mobile Legends
    public string Currency { get; set; } = string.Empty; // V-Bucks, Robux, UC, etc.
    public string CurrencyShort { get; set; } = string.Empty;
    public string? ImageUrl { get; set; } // Card image for game selection page
    public string? IconUrl { get; set; } // Small icon for sidebar
    public string Icon { get; set; } = "🎮"; // Emoji fallback
    public string Gradient { get; set; } = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";
    public string Color { get; set; } = "#667eea";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual ICollection<GameProduct> Products { get; set; } = new List<GameProduct>();
}

/// <summary>
/// Represents a product within a game (currency pack, pass, subscription)
/// </summary>
public class GameProduct
{
    public int Id { get; set; }
    public int? GameId { get; set; }
    public int? GiftCardId { get; set; }

    // Product info
    public string Name { get; set; } = string.Empty; // "1000 V-Bucks"
    public string? Amount { get; set; } // "1000"
    public string? Bonus { get; set; } // "+100" bonus amount
    public string? TotalDisplay { get; set; } // "1100 V-Bucks" (displayed)

    // Pricing
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public int Discount { get; set; } // Discount percentage

    // Categorization
    public GameProductType ProductType { get; set; } = GameProductType.Currency;
    public string? Multiplier { get; set; } // "x2", "x3" for packs
    public string? Region { get; set; } // "RU", "USA" for region-specific cards

    // Display
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }

    // Stock
    public int StockQuantity { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public virtual Game? Game { get; set; }
    public virtual GiftCard? GiftCard { get; set; }
    public virtual ICollection<ProductKey> ProductKeys { get; set; } = new List<ProductKey>();
}

public enum GameProductType
{
    Currency = 1,      // V-Bucks, Robux, UC, Diamonds, etc.
    Pack = 2,          // x2, x3 packs, All Pack
    Pass = 3,          // Battle Pass, Royale Pass, Starlight
    Subscription = 4,  // Monthly subscriptions, Prime
    GiftCard = 5,      // Gift cards with codes
    Promo = 6          // Special promo items
}
