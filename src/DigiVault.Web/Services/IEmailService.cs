namespace DigiVault.Web.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
    Task SendVerificationCodeAsync(string to, string code, string? userName = null);
    Task SendOrderConfirmationAsync(string to, string orderNumber, decimal amount);
}
