using DigiVault.Core.Enums;

namespace DigiVault.Core.Models.Payment;

/// <summary>
/// Результат валидации webhook от платежного провайдера
/// </summary>
public class WebhookValidationResult
{
    /// <summary>Валидна ли подпись/данные</summary>
    public bool IsValid { get; set; }

    /// <summary>ID транзакции</summary>
    public string? TransactionId { get; set; }

    /// <summary>Новый статус платежа</summary>
    public PaymentStatus? NewStatus { get; set; }

    /// <summary>Сумма платежа (для сверки)</summary>
    public decimal? Amount { get; set; }

    /// <summary>Сообщение об ошибке валидации</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Сырые данные webhook (для логирования)</summary>
    public string? RawData { get; set; }

    /// <summary>
    /// Provider-specific plain-text response that must be written back to the
    /// HTTP response when sending 200 OK. PaymentLink, for example, requires
    /// the response body to be the literal transID value to confirm acceptance
    /// of the payment - any other body causes them to abort the charge.
    /// When null, the controller uses its default JSON envelope.
    /// </summary>
    public string? ResponseBody { get; set; }

    public static WebhookValidationResult Valid(string transactionId, PaymentStatus status, decimal amount)
    {
        return new WebhookValidationResult
        {
            IsValid = true,
            TransactionId = transactionId,
            NewStatus = status,
            Amount = amount
        };
    }

    public static WebhookValidationResult Invalid(string errorMessage)
    {
        return new WebhookValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage
        };
    }
}
