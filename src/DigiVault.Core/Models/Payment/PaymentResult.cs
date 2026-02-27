using DigiVault.Core.Enums;

namespace DigiVault.Core.Models.Payment;

/// <summary>
/// Результат создания платежа
/// </summary>
public class PaymentResult
{
    /// <summary>Успешно ли создан платеж</summary>
    public bool Success { get; set; }

    /// <summary>ID транзакции в нашей системе</summary>
    public string? TransactionId { get; set; }

    /// <summary>ID транзакции у провайдера</summary>
    public string? ProviderTransactionId { get; set; }

    /// <summary>URL для редиректа на страницу оплаты</summary>
    public string? RedirectUrl { get; set; }

    /// <summary>Статус платежа</summary>
    public PaymentStatus Status { get; set; }

    /// <summary>Сообщение об ошибке</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Код ошибки</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Дополнительные данные от провайдера</summary>
    public Dictionary<string, string>? ProviderData { get; set; }

    public static PaymentResult Successful(string transactionId, string? redirectUrl = null, string? providerTransactionId = null)
    {
        return new PaymentResult
        {
            Success = true,
            TransactionId = transactionId,
            ProviderTransactionId = providerTransactionId,
            RedirectUrl = redirectUrl,
            Status = PaymentStatus.Pending
        };
    }

    public static PaymentResult Failed(string errorMessage, string? errorCode = null)
    {
        return new PaymentResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            Status = PaymentStatus.Failed
        };
    }
}
