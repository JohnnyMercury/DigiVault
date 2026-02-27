namespace DigiVault.Core.Enums;

/// <summary>
/// Статус платежной транзакции
/// </summary>
public enum PaymentStatus
{
    /// <summary>Платеж создан, ожидает обработки</summary>
    Pending = 0,

    /// <summary>Платеж в процессе обработки провайдером</summary>
    Processing = 1,

    /// <summary>Платеж успешно завершен</summary>
    Completed = 2,

    /// <summary>Платеж не прошел</summary>
    Failed = 3,

    /// <summary>Платеж отменен пользователем</summary>
    Cancelled = 4,

    /// <summary>Платеж возвращен</summary>
    Refunded = 5,

    /// <summary>Истек срок ожидания платежа</summary>
    Expired = 6
}
