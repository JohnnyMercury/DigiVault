using DigiVault.Core.Enums;

namespace DigiVault.Core.Models.Payment;

/// <summary>
/// Результат проверки статуса платежа
/// </summary>
public class PaymentStatusResult
{
    /// <summary>ID транзакции</summary>
    public required string TransactionId { get; set; }

    /// <summary>Статус платежа</summary>
    public PaymentStatus Status { get; set; }

    /// <summary>Сумма платежа</summary>
    public decimal Amount { get; set; }

    /// <summary>Валюта</summary>
    public string Currency { get; set; } = "RUB";

    /// <summary>Дата/время последнего обновления</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Сообщение от провайдера</summary>
    public string? Message { get; set; }

    /// <summary>Дополнительные данные</summary>
    public Dictionary<string, string>? ProviderData { get; set; }

    /// <summary>Платеж завершен (успешно или с ошибкой)</summary>
    public bool IsFinalized => Status is PaymentStatus.Completed
                                        or PaymentStatus.Failed
                                        or PaymentStatus.Cancelled
                                        or PaymentStatus.Refunded
                                        or PaymentStatus.Expired;
}
