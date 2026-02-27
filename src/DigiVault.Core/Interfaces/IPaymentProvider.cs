using DigiVault.Core.Enums;
using DigiVault.Core.Models.Payment;

namespace DigiVault.Core.Interfaces;

/// <summary>
/// Интерфейс платежного провайдера.
/// Каждый провайдер (Stripe, YooKassa, и т.д.) реализует этот интерфейс.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>Уникальное имя провайдера</summary>
    string Name { get; }

    /// <summary>Отображаемое название</summary>
    string DisplayName { get; }

    /// <summary>Поддерживаемые методы оплаты</summary>
    IReadOnlyList<PaymentMethod> SupportedMethods { get; }

    /// <summary>Активен ли провайдер</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Создать платеж
    /// </summary>
    Task<PaymentResult> CreatePaymentAsync(PaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Проверить статус платежа
    /// </summary>
    Task<PaymentStatusResult> GetPaymentStatusAsync(string transactionId, CancellationToken ct = default);

    /// <summary>
    /// Валидировать webhook callback
    /// </summary>
    Task<WebhookValidationResult> ValidateWebhookAsync(
        Dictionary<string, string> headers,
        string body,
        CancellationToken ct = default);

    /// <summary>
    /// Отменить/вернуть платеж (если поддерживается)
    /// </summary>
    Task<PaymentResult> RefundAsync(string transactionId, decimal? amount = null, CancellationToken ct = default);

    /// <summary>
    /// Поддерживает ли провайдер возвраты
    /// </summary>
    bool SupportsRefund { get; }
}
