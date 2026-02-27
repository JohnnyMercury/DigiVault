namespace DigiVault.Core.Entities;

/// <summary>
/// Конфигурация платежного провайдера (хранится в БД)
/// </summary>
public class PaymentProviderConfig
{
    public int Id { get; set; }

    /// <summary>Уникальное имя провайдера</summary>
    public required string Name { get; set; }

    /// <summary>Отображаемое название</summary>
    public required string DisplayName { get; set; }

    /// <summary>Активен ли провайдер</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Приоритет (для сортировки)</summary>
    public int Priority { get; set; }

    /// <summary>API ключ (зашифрован)</summary>
    public string? ApiKey { get; set; }

    /// <summary>Секретный ключ (зашифрован)</summary>
    public string? SecretKey { get; set; }

    /// <summary>ID магазина/мерчанта</summary>
    public string? MerchantId { get; set; }

    /// <summary>Тестовый режим</summary>
    public bool IsTestMode { get; set; }

    /// <summary>Дополнительные настройки (JSON)</summary>
    public string? Settings { get; set; }

    /// <summary>Комиссия провайдера (%)</summary>
    public decimal? Commission { get; set; }

    /// <summary>Минимальная сумма платежа</summary>
    public decimal? MinAmount { get; set; }

    /// <summary>Максимальная сумма платежа</summary>
    public decimal? MaxAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
