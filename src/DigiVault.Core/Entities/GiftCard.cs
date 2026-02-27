namespace DigiVault.Core.Entities;

public class GiftCard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string Icon { get; set; } = "🎁";
    public string Gradient { get; set; } = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";
    public GiftCardCategory Category { get; set; } = GiftCardCategory.Gaming;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual ICollection<GameProduct> Products { get; set; } = new List<GameProduct>();
}

public enum GiftCardCategory
{
    Gaming = 1,
    Streaming = 2
}
