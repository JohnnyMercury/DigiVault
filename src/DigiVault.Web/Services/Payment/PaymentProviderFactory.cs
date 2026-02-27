using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;

namespace DigiVault.Web.Services.Payment;

/// <summary>
/// Фабрика платежных провайдеров
/// </summary>
public class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly ILogger<PaymentProviderFactory> _logger;

    public PaymentProviderFactory(
        IEnumerable<IPaymentProvider> providers,
        ILogger<PaymentProviderFactory> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public IPaymentProvider? GetProvider(string providerName)
    {
        var provider = _providers.FirstOrDefault(p =>
            p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            _logger.LogWarning("Payment provider '{ProviderName}' not found", providerName);
        }

        return provider;
    }

    public IPaymentProvider? GetProviderForMethod(PaymentMethod method)
    {
        // Возвращает первый активный провайдер для метода (по приоритету)
        return _providers
            .Where(p => p.IsEnabled && p.SupportedMethods.Contains(method))
            .FirstOrDefault();
    }

    public IReadOnlyList<IPaymentProvider> GetActiveProviders()
    {
        return _providers.Where(p => p.IsEnabled).ToList();
    }

    public IReadOnlyList<IPaymentProvider> GetProvidersForMethod(PaymentMethod method)
    {
        return _providers
            .Where(p => p.IsEnabled && p.SupportedMethods.Contains(method))
            .ToList();
    }

    public bool HasProviderForMethod(PaymentMethod method)
    {
        return _providers.Any(p => p.IsEnabled && p.SupportedMethods.Contains(method));
    }
}
