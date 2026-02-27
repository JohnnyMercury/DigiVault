using DigiVault.Core.Enums;

namespace DigiVault.Core.Models.Payment;

/// <summary>
/// Запрос на создание платежа
/// </summary>
public class PaymentRequest
{
    /// <summary>ID пользователя</summary>
    public required string UserId { get; set; }

    /// <summary>Сумма платежа</summary>
    public decimal Amount { get; set; }

    /// <summary>Валюта (ISO 4217)</summary>
    public string Currency { get; set; } = "RUB";

    /// <summary>Метод оплаты</summary>
    public PaymentMethod Method { get; set; }

    /// <summary>Email пользователя для чека</summary>
    public string? Email { get; set; }

    /// <summary>Описание платежа</summary>
    public string? Description { get; set; }

    /// <summary>ID заказа (если оплата заказа)</summary>
    public int? OrderId { get; set; }

    /// <summary>URL для редиректа после успешной оплаты</summary>
    public string? SuccessUrl { get; set; }

    /// <summary>URL для редиректа при отмене</summary>
    public string? CancelUrl { get; set; }

    /// <summary>URL для webhook уведомлений</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>Дополнительные данные (для провайдера)</summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>IP адрес клиента</summary>
    public string? ClientIp { get; set; }
}
