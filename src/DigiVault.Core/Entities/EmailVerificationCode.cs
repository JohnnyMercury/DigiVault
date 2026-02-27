namespace DigiVault.Core.Entities;

public class EmailVerificationCode
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
    public int AttemptCount { get; set; }
    public string? IpAddress { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
}
