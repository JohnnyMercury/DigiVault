namespace DigiVault.Core.Entities;

public class WalletTransaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceAfterTransaction { get; set; }
    public WalletTransactionType Type { get; set; }
    public string? Description { get; set; }
    public string? Reference { get; set; }
    public WalletTransactionStatus Status { get; set; } = WalletTransactionStatus.Completed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    public virtual ApplicationUser User { get; set; } = null!;
}

public enum WalletTransactionType
{
    Deposit,
    Purchase,
    Refund,
    Bonus,
    Withdrawal
}

public enum WalletTransactionStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Cancelled
}
