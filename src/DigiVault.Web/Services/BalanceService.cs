using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

public class BalanceService : IBalanceService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BalanceService> _logger;

    public BalanceService(ApplicationDbContext context, ILogger<BalanceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<decimal> GetBalanceAsync(string userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user?.Balance ?? 0;
    }

    public async Task<bool> AddFundsAsync(string userId, decimal amount, string? description = null, string? reference = null)
    {
        if (amount <= 0) return false;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            user.Balance += amount;

            _context.Set<WalletTransaction>().Add(new WalletTransaction
            {
                UserId = userId,
                Amount = amount,
                BalanceAfterTransaction = user.Balance,
                Type = WalletTransactionType.Deposit,
                Description = description ?? "Пополнение баланса",
                Reference = reference,
                Status = WalletTransactionStatus.Completed,
                ProcessedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Added {Amount} to user {UserId} balance. New balance: {Balance}",
                amount, userId, user.Balance);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to add funds for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> DeductFundsAsync(string userId, decimal amount, string? description = null, string? reference = null)
    {
        if (amount <= 0) return false;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Balance < amount) return false;

            user.Balance -= amount;

            _context.Set<WalletTransaction>().Add(new WalletTransaction
            {
                UserId = userId,
                Amount = -amount,
                BalanceAfterTransaction = user.Balance,
                Type = WalletTransactionType.Purchase,
                Description = description ?? "Покупка",
                Reference = reference,
                Status = WalletTransactionStatus.Completed,
                ProcessedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Deducted {Amount} from user {UserId} balance. New balance: {Balance}",
                amount, userId, user.Balance);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to deduct funds for user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> RefundAsync(string userId, decimal amount, string? description = null, string? reference = null)
    {
        if (amount <= 0) return false;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            user.Balance += amount;

            _context.Set<WalletTransaction>().Add(new WalletTransaction
            {
                UserId = userId,
                Amount = amount,
                BalanceAfterTransaction = user.Balance,
                Type = WalletTransactionType.Refund,
                Description = description ?? "Возврат средств",
                Reference = reference,
                Status = WalletTransactionStatus.Completed,
                ProcessedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Refunded {Amount} to user {UserId}. New balance: {Balance}",
                amount, userId, user.Balance);
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to refund for user {UserId}", userId);
            return false;
        }
    }

    public async Task<List<WalletTransaction>> GetTransactionHistoryAsync(string userId, int limit = 50)
    {
        return await _context.Set<WalletTransaction>()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}
