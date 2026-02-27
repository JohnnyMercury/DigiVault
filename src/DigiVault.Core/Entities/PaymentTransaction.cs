using DigiVault.Core.Enums;

namespace DigiVault.Core.Entities;

/// <summary>
/// Платежная транзакция
/// </summary>
public class PaymentTransaction
{
    public int Id { get; set; }

    /// <summary>Уникальный ID транзакции в нашей системе</summary>
    public required string TransactionId { get; set; }

    /// <summary>ID транзакции у провайдера</summary>
    public string? ProviderTransactionId { get; set; }

    /// <summary>ID пользователя</summary>
    public required string UserId { get; set; }

    /// <summary>Пользователь</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>ID заказа (если оплата заказа)</summary>
    public int? OrderId { get; set; }

    /// <summary>Заказ</summary>
    public Order? Order { get; set; }

    /// <summary>Имя провайдера</summary>
    public required string ProviderName { get; set; }

    /// <summary>Метод оплаты</summary>
    public PaymentMethod Method { get; set; }

    /// <summary>Сумма</summary>
    public decimal Amount { get; set; }

    /// <summary>Валюта</summary>
    public string Currency { get; set; } = "RUB";

    /// <summary>Статус</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>Описание</summary>
    public string? Description { get; set; }

    /// <summary>Сообщение об ошибке</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>IP адрес клиента</summary>
    public string? ClientIp { get; set; }

    /// <summary>Данные от провайдера (JSON)</summary>
    public string? ProviderData { get; set; }

    /// <summary>Метаданные (JSON)</summary>
    public string? Metadata { get; set; }

    /// <summary>Дата создания</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Дата последнего обновления</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Дата завершения (успех/ошибка)</summary>
    public DateTime? CompletedAt { get; set; }
}
