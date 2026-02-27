using DigiVault.Core.Entities;

namespace DigiVault.Web.Services;

public interface IBalanceService
{
    Task<decimal> GetBalanceAsync(string userId);
    Task<bool> AddFundsAsync(string userId, decimal amount, string? description = null, string? reference = null);
    Task<bool> DeductFundsAsync(string userId, decimal amount, string? description = null, string? reference = null);
    Task<bool> RefundAsync(string userId, decimal amount, string? description = null, string? reference = null);
    Task<List<WalletTransaction>> GetTransactionHistoryAsync(string userId, int limit = 50);
}
