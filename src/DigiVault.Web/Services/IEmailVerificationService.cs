namespace DigiVault.Web.Services;

public interface IEmailVerificationService
{
    Task<VerificationResult> GenerateAndSendCodeAsync(string userId, string email, string? ipAddress = null);
    Task<VerificationResult> VerifyCodeAsync(string userId, string code);
    Task<bool> IsResendAllowedAsync(string userId);
    Task CleanupExpiredCodesAsync();
}

public class VerificationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? RemainingAttempts { get; set; }
}
