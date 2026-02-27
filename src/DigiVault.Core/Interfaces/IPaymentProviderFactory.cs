using DigiVault.Core.Enums;

namespace DigiVault.Core.Interfaces;

/// <summary>
/// Фабрика для получения платежных провайдеров
/// </summary>
public interface IPaymentProviderFactory
{
    /// <summary>
    /// Получить провайдер по имени
    /// </summary>
    IPaymentProvider? GetProvider(string providerName);

    /// <summary>
    /// Получить провайдер для метода оплаты
    /// </summary>
    IPaymentProvider? GetProviderForMethod(PaymentMethod method);

    /// <summary>
    /// Получить все активные провайдеры
    /// </summary>
    IReadOnlyList<IPaymentProvider> GetActiveProviders();

    /// <summary>
    /// Получить провайдеры, поддерживающие метод оплаты
    /// </summary>
    IReadOnlyList<IPaymentProvider> GetProvidersForMethod(PaymentMethod method);

    /// <summary>
    /// Проверить, есть ли активный провайдер для метода
    /// </summary>
    bool HasProviderForMethod(PaymentMethod method);
}
