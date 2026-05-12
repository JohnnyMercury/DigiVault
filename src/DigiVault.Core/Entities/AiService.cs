namespace DigiVault.Core.Entities;

/// <summary>
/// Top-level AI-service brand (ChatGPT, Claude, Midjourney, Cursor, etc.).
/// Holds the marketing card + a navigation list of <see cref="GameProduct"/>
/// rows that represent concrete tariffs (Plus monthly, Pro annual, Team seat).
///
/// Modelled on <see cref="VpnProvider"/> because the UX is the same: pick a
/// brand → pick a plan → checkout. Delivery for most AI tariffs runs through
/// the existing «оператор подтверждает в Telegram» flow (ContactSupport
/// credential) — the resale market mostly hands over fresh accounts or adds
/// the buyer's email to a paid team workspace, both of which are manual.
/// </summary>
public class AiService
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Tagline { get; set; } // "ChatGPT-4 / GPT-4o / o1 / image generation"
    public string? Features { get; set; } // JSON array, e.g. ["Доступ к GPT-4o","DALL-E","Расширенный контекст"]
    public string? ImageUrl { get; set; }
    public string Icon { get; set; } = "🤖";
    public string Gradient { get; set; } = "linear-gradient(135deg, #10a37f 0%, #1a7f64 100%)";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual ICollection<GameProduct> Products { get; set; } = new List<GameProduct>();
}
