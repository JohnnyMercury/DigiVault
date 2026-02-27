using System.Net;
using System.Net.Mail;

namespace DigiVault.Web.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        try
        {
            var smtpHost = _config["Email:SmtpHost"] ?? "smtp.gmail.com";
            var smtpPort = int.Parse(_config["Email:SmtpPort"] ?? "587");
            var fromEmail = _config["Email:FromEmail"] ?? throw new InvalidOperationException("Email:FromEmail not configured");
            var fromPassword = _config["Email:FromPassword"] ?? throw new InvalidOperationException("Email:FromPassword not configured");
            var fromName = _config["Email:FromName"] ?? "DigiVault";

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(fromEmail, fromPassword),
                EnableSsl = true
            };

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);

            await client.SendMailAsync(message);
            _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            throw;
        }
    }

    public async Task SendVerificationCodeAsync(string to, string code, string? userName = null)
    {
        var name = userName ?? "пользователь";
        var html = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
            <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;'>
                <h1 style='color: white; margin: 0;'>DigiVault</h1>
            </div>
            <div style='background: #1a1a2e; color: #e0e0e0; padding: 30px; border-radius: 0 0 10px 10px;'>
                <p>Здравствуйте, {name}!</p>
                <p>Ваш код подтверждения:</p>
                <div style='text-align: center; margin: 20px 0;'>
                    <span style='font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #667eea; background: #16213e; padding: 15px 30px; border-radius: 8px;'>{code}</span>
                </div>
                <p style='color: #888;'>Код действителен 15 минут.</p>
                <p style='color: #888; font-size: 12px;'>Если вы не запрашивали этот код, просто проигнорируйте это письмо.</p>
            </div>
        </div>";

        await SendEmailAsync(to, $"Код подтверждения: {code}", html);
    }

    public async Task SendOrderConfirmationAsync(string to, string orderNumber, decimal amount)
    {
        var html = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
            <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; text-align: center; border-radius: 10px 10px 0 0;'>
                <h1 style='color: white; margin: 0;'>DigiVault</h1>
            </div>
            <div style='background: #1a1a2e; color: #e0e0e0; padding: 30px; border-radius: 0 0 10px 10px;'>
                <h2>Заказ подтверждён</h2>
                <p>Номер заказа: <strong>{orderNumber}</strong></p>
                <p>Сумма: <strong>{amount:N2} ₽</strong></p>
                <p style='color: #888;'>Спасибо за покупку!</p>
            </div>
        </div>";

        await SendEmailAsync(to, $"Заказ {orderNumber} подтверждён", html);
    }
}
