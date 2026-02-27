using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

public class EmailVerificationService : IEmailVerificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailVerificationService> _logger;
    private const int MaxAttempts = 5;
    private const int CodeExpirationMinutes = 15;
    private const int ResendCooldownSeconds = 60;

    public EmailVerificationService(
        ApplicationDbContext context,
        IEmailService emailService,
        ILogger<EmailVerificationService> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<VerificationResult> GenerateAndSendCodeAsync(string userId, string email, string? ipAddress = null)
    {
        try
        {
            if (!await IsResendAllowedAsync(userId))
            {
                return new VerificationResult
                {
                    Success = false,
                    Message = "Подождите перед повторной отправкой кода"
                };
            }

            // Invalidate existing codes
            var existingCodes = await _context.EmailVerificationCodes
                .Where(c => c.UserId == userId && !c.IsUsed)
                .ToListAsync();

            foreach (var c in existingCodes)
                c.IsUsed = true;

            // Generate new 6-digit code
            var code = Random.Shared.Next(100000, 999999).ToString();

            var verificationCode = new EmailVerificationCode
            {
                UserId = userId,
                Code = code,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(CodeExpirationMinutes),
                IpAddress = ipAddress
            };

            _context.EmailVerificationCodes.Add(verificationCode);
            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(userId);
            await _emailService.SendVerificationCodeAsync(email, code, user?.UserName);

            _logger.LogInformation("Verification code sent to {Email} for user {UserId}", email, userId);

            return new VerificationResult
            {
                Success = true,
                Message = "Код подтверждения отправлен на ваш email"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending verification code for user {UserId}", userId);
            return new VerificationResult
            {
                Success = false,
                Message = "Не удалось отправить код. Попробуйте позже."
            };
        }
    }

    public async Task<VerificationResult> VerifyCodeAsync(string userId, string code)
    {
        try
        {
            var storedCode = await _context.EmailVerificationCodes
                .Where(c => c.UserId == userId && !c.IsUsed && c.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();

            if (storedCode == null)
            {
                return new VerificationResult
                {
                    Success = false,
                    Message = "Код истёк или не найден. Запросите новый."
                };
            }

            storedCode.AttemptCount++;

            if (storedCode.AttemptCount > MaxAttempts)
            {
                storedCode.IsUsed = true;
                await _context.SaveChangesAsync();
                return new VerificationResult
                {
                    Success = false,
                    Message = "Превышено количество попыток. Запросите новый код."
                };
            }

            if (storedCode.Code == code)
            {
                storedCode.IsUsed = true;

                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    user.EmailConfirmed = true;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Email verified for user {UserId}", userId);
                return new VerificationResult
                {
                    Success = true,
                    Message = "Email успешно подтверждён!"
                };
            }

            await _context.SaveChangesAsync();

            var remaining = MaxAttempts - storedCode.AttemptCount;
            return new VerificationResult
            {
                Success = false,
                Message = $"Неверный код. Осталось попыток: {remaining}",
                RemainingAttempts = remaining
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying code for user {UserId}", userId);
            return new VerificationResult
            {
                Success = false,
                Message = "Ошибка верификации. Попробуйте позже."
            };
        }
    }

    public async Task<bool> IsResendAllowedAsync(string userId)
    {
        var lastCode = await _context.EmailVerificationCodes
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastCode == null) return true;
        return DateTime.UtcNow > lastCode.CreatedAt.AddSeconds(ResendCooldownSeconds);
    }

    public async Task CleanupExpiredCodesAsync()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var deleted = await _context.EmailVerificationCodes
            .Where(c => c.ExpiresAt < cutoff)
            .ExecuteDeleteAsync();

        if (deleted > 0)
            _logger.LogInformation("Cleaned up {Count} expired verification codes", deleted);
    }
}
