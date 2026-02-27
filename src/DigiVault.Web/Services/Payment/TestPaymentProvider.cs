using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;

namespace DigiVault.Web.Services.Payment;

/// <summary>
/// Тестовый провайдер для разработки.
/// Автоматически одобряет платежи без реальной обработки.
/// </summary>
public class TestPaymentProvider : IPaymentProvider
{
    private readonly ILogger<TestPaymentProvider> _logger;
    private readonly IConfiguration _configuration;

    public TestPaymentProvider(
        ILogger<TestPaymentProvider> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public string Name => "test";
    public string DisplayName => "Тестовый провайдер";
    public bool SupportsRefund => true;

    public bool IsEnabled => _configuration.GetValue<bool>("Payment:TestProvider:Enabled", true);

    public IReadOnlyList<PaymentMethod> SupportedMethods => new[]
    {
        PaymentMethod.Card,
        PaymentMethod.SBP,
        PaymentMethod.YooMoney,
        PaymentMethod.Balance
    };

    public Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "TestProvider: Creating payment for user {UserId}, amount {Amount} {Currency}",
            request.UserId, request.Amount, request.Currency);

        // Валидация
        if (request.Amount <= 0)
        {
            return Task.FromResult(PaymentResult.Failed("Сумма должна быть больше 0"));
        }

        if (request.Amount > 100000)
        {
            return Task.FromResult(PaymentResult.Failed("Максимальная сумма 100,000"));
        }

        // Генерируем ID транзакции
        var transactionId = $"TEST-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32];

        // В тестовом режиме сразу возвращаем успех
        var result = PaymentResult.Successful(
            transactionId: transactionId,
            redirectUrl: request.SuccessUrl ?? "/Account/Deposit?success=true",
            providerTransactionId: $"PROV-{Guid.NewGuid():N}"[..20]
        );

        result.Status = PaymentStatus.Completed; // Тестовый - сразу completed
        result.ProviderData = new Dictionary<string, string>
        {
            ["test_mode"] = "true",
            ["auto_approved"] = "true"
        };

        _logger.LogInformation(
            "TestProvider: Payment created successfully. TransactionId: {TransactionId}",
            transactionId);

        return Task.FromResult(result);
    }

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default)
    {
        _logger.LogInformation("TestProvider: Checking status for {TransactionId}", transactionId);

        // Тестовый провайдер всегда возвращает Completed
        return Task.FromResult(new PaymentStatusResult
        {
            TransactionId = transactionId,
            Status = PaymentStatus.Completed,
            Amount = 0, // Неизвестно без БД
            UpdatedAt = DateTime.UtcNow,
            Message = "Test payment completed"
        });
    }

    public Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers,
        string body,
        CancellationToken ct = default)
    {
        _logger.LogInformation("TestProvider: Validating webhook");

        // Тестовый провайдер принимает любой webhook
        return Task.FromResult(WebhookValidationResult.Valid(
            transactionId: "unknown",
            status: PaymentStatus.Completed,
            amount: 0
        ));
    }

    public Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "TestProvider: Refunding {TransactionId}, amount: {Amount}",
            transactionId, amount?.ToString() ?? "full");

        var refundId = $"REFUND-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..32];

        return Task.FromResult(PaymentResult.Successful(refundId));
    }
}
