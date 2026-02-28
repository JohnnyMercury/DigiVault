namespace DigiVault.Core.Entities;

public class VpnProvider
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Tagline { get; set; } // "5500+ серверов в 60 странах"
    public string? Features { get; set; } // JSON array of features: ["Защита до 6 устройств", "Блокировка рекламы"]
    public string? ImageUrl { get; set; }
    public string Icon { get; set; } = "🔒";
    public string Gradient { get; set; } = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual ICollection<GameProduct> Products { get; set; } = new List<GameProduct>();
}
