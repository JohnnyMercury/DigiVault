namespace DigiVault.Core.Entities;

public class HeroBanner
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? ButtonText { get; set; }
    public string? ButtonUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string Gradient { get; set; } = "linear-gradient(135deg, #1a1a2e 0%, #16213e 100%)";
    public string SubtitleColor { get; set; } = "#667eea";
    public string ButtonClass { get; set; } = "btn-primary";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
