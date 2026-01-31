using DigiVault.Core.Enums;

namespace DigiVault.Core.Entities;

public class Transaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string? Description { get; set; }
    public int? OrderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Order? Order { get; set; }
}
