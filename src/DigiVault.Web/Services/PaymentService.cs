using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Core.Models.Payment;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DigiVault.Web.Services;

public interface IPaymentService
{
    /// <summary>Создать платеж для пополнения баланса.
    /// <paramref name="siteBaseUrl"/> — абсолютный базовый URL сайта (https://host),
    /// нужен Enot/PaymentLink для построения success/fail/webhook callbacks.
    /// <paramref name="providerName"/> — явное имя PSP из step-2 picker'а.</summary>
    Task<PaymentResult> CreateDepositAsync(string userId, decimal amount, PaymentMethod method,
        string? clientIp = null, string? siteBaseUrl = null, string? providerName = null);

    /// <summary>
    /// Обработать webhook от провайдера. Возвращает <see cref="WebhookValidationResult"/>
    /// чтобы контроллер мог:
    ///   • вернуть <see cref="WebhookValidationResult.ResponseBody"/> как plain-text
    ///     (PaymentLink требует raw transID в ответе, не JSON);
    ///   • найти OrderId по <see cref="WebhookValidationResult.TransactionId"/>
    ///     для запуска фулфилмента сразу же.
    /// Возвращает null, если провайдер не зарегистрирован.
    /// </summary>
    Task<WebhookValidationResult?> ProcessWebhookAsync(string providerName, Dictionary<string, string> headers, string body);

    /// <summary>Получить статус платежа</summary>
    Task<PaymentStatusResult?> GetPaymentStatusAsync(string transactionId);

    /// <summary>Завершить платеж (зачислить средства)</summary>
    Task<bool> CompletePaymentAsync(string transactionId);

    /// <summary>Старый метод для совместимости</summary>
    Task<(bool Success, string? TransactionId, string? Error)> ProcessDepositAsync(string userId, decimal amount, string paymentMethod);
}

public class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _context;
    private readonly IPaymentProviderFactory _providerFactory;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        ApplicationDbContext context,
        IPaymentProviderFactory providerFactory,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async Task<PaymentResult> CreateDepositAsync(
        string userId,
        decimal amount,
        PaymentMethod method,
        string? clientIp = null,
        string? siteBaseUrl = null,
        string? providerName = null)
    {
        _logger.LogInformation(
            "Creating deposit for user {UserId}, amount {Amount}, method {Method}",
            userId, amount, method);

        // Валидация
        if (amount <= 0)
            return PaymentResult.Failed("Сумма должна быть больше 0");

        if (amount > 100000)
            return PaymentResult.Failed("Максимальная сумма пополнения 100,000 ₽");

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return PaymentResult.Failed("Пользователь не найден");

        // Получаем провайдер. Если вызывающий явно указал имя (с step-2
        // picker), уважаем выбор; иначе fallback на первый подходящий.
        IPaymentProvider? provider = null;
        if (!string.IsNullOrWhiteSpace(providerName))
        {
            provider = _providerFactory.GetProvider(providerName);
            if (provider != null && !provider.SupportedMethods.Contains(method))
                provider = null;
        }
        provider ??= _providerFactory.GetProviderForMethod(method);
        if (provider == null)
        {
            _logger.LogWarning("No provider found for method {Method}", method);
            return PaymentResult.Failed("Метод оплаты временно недоступен");
        }

        // Build absolute callbacks. Enot validates these as URIs server-side
        // and rejects relative paths («Поле fail_url имеет ошибочный формат»).
        // Fallback for the legacy callsites that don't pass siteBaseUrl: use
        // the production host so we never send Enot a relative path.
        var siteBase = !string.IsNullOrWhiteSpace(siteBaseUrl)
            ? siteBaseUrl!.TrimEnd('/')
            : "https://key-zona.com";

        // Создаем запрос
        var request = new PaymentRequest
        {
            UserId = userId,
            Amount = amount,
            Currency = "RUB",
            Method = method,
            Email = user.Email,
            Description = $"Пополнение баланса DigiVault",
            SuccessUrl = $"{siteBase}/Account/Deposit?success=true",
            CancelUrl  = $"{siteBase}/Account/Deposit?cancelled=true",
            WebhookUrl = $"{siteBase}/api/webhooks/{provider.Name}",
            ClientIp = clientIp
        };

        // Создаем платеж у провайдера
        var result = await provider.CreatePaymentAsync(request);

        if (!result.Success)
        {
            _logger.LogWarning(
                "Payment creation failed for user {UserId}: {Error}",
                userId, result.ErrorMessage);
            return result;
        }

        // Сохраняем транзакцию в БД
        var transaction = new PaymentTransaction
        {
            TransactionId = result.TransactionId!,
            ProviderTransactionId = result.ProviderTransactionId,
            UserId = userId,
            ProviderName = provider.Name,
            Method = method,
            Amount = amount,
            Currency = "RUB",
            Status = result.Status,
            Description = request.Description,
            ClientIp = clientIp,
            ProviderData = result.ProviderData != null
                ? JsonSerializer.Serialize(result.ProviderData)
                : null
        };

        _context.PaymentTransactions.Add(transaction);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Payment transaction created: {TransactionId}, provider: {Provider}",
            result.TransactionId, provider.Name);

        // Если провайдер сразу завершил платеж (тестовый режим)
        if (result.Status == PaymentStatus.Completed)
        {
            await CompletePaymentAsync(result.TransactionId!);
        }

        return result;
    }

    public async Task<WebhookValidationResult?> ProcessWebhookAsync(
        string providerName,
        Dictionary<string, string> headers,
        string body)
    {
        _logger.LogInformation("Processing webhook from {Provider}", providerName);

        var provider = _providerFactory.GetProvider(providerName);
        if (provider == null)
        {
            _logger.LogWarning("Unknown provider in webhook: {Provider}", providerName);
            return null;
        }

        var validationResult = await provider.ValidateWebhookAsync(headers, body);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Webhook validation failed for {Provider}: {Error}",
                providerName, validationResult.ErrorMessage);
            return validationResult;
        }

        if (string.IsNullOrEmpty(validationResult.TransactionId))
        {
            _logger.LogWarning("No transaction ID in webhook from {Provider}", providerName);
            return validationResult;
        }

        // Обновляем статус транзакции
        var transaction = await _context.PaymentTransactions
            .FirstOrDefaultAsync(t => t.TransactionId == validationResult.TransactionId
                                   || t.ProviderTransactionId == validationResult.TransactionId);

        if (transaction == null)
        {
            _logger.LogWarning(
                "Transaction not found for webhook: {TransactionId}",
                validationResult.TransactionId);
            return validationResult;
        }

        if (validationResult.NewStatus.HasValue)
        {
            transaction.Status = validationResult.NewStatus.Value;
            transaction.UpdatedAt = DateTime.UtcNow;

            if (validationResult.NewStatus == PaymentStatus.Completed)
            {
                transaction.CompletedAt = DateTime.UtcNow;

                // Bump linked Order from Pending → Processing so the Fulfilment
                // Sweep safety-net (which runs every 30s and now only picks
                // Processing orders) will deliver the goods even if the inline
                // DeliverOrderAsync call from WebhooksController fails.
                if (transaction.OrderId.HasValue)
                {
                    var order = await _context.Orders.FindAsync(transaction.OrderId.Value);
                    if (order != null && order.Status == OrderStatus.Pending)
                    {
                        order.Status = OrderStatus.Processing;
                    }
                }

                await CompletePaymentInternalAsync(transaction);
            }

            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Webhook processed for {TransactionId}, new status: {Status}",
            transaction.TransactionId, transaction.Status);

        return validationResult;
    }

    public async Task<PaymentStatusResult?> GetPaymentStatusAsync(string transactionId)
    {
        var transaction = await _context.PaymentTransactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

        if (transaction == null)
            return null;

        return new PaymentStatusResult
        {
            TransactionId = transaction.TransactionId,
            Status = transaction.Status,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            UpdatedAt = transaction.UpdatedAt
        };
    }

    public async Task<bool> CompletePaymentAsync(string transactionId)
    {
        var transaction = await _context.PaymentTransactions
            .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

        if (transaction == null)
        {
            _logger.LogWarning("Transaction not found: {TransactionId}", transactionId);
            return false;
        }

        if (transaction.Status == PaymentStatus.Completed)
        {
            // Уже завершен - проверяем зачислен ли баланс
            var existingTx = await _context.Transactions
                .AnyAsync(t => t.Description != null && t.Description.Contains(transactionId));

            if (existingTx)
            {
                _logger.LogInformation("Payment already completed: {TransactionId}", transactionId);
                return true;
            }
        }

        transaction.Status = PaymentStatus.Completed;
        transaction.CompletedAt = DateTime.UtcNow;
        transaction.UpdatedAt = DateTime.UtcNow;

        await CompletePaymentInternalAsync(transaction);
        await _context.SaveChangesAsync();

        return true;
    }

    private async Task CompletePaymentInternalAsync(PaymentTransaction transaction)
    {
        var user = await _context.Users.FindAsync(transaction.UserId);
        if (user == null)
        {
            _logger.LogError("User not found for payment: {UserId}", transaction.UserId);
            return;
        }

        // Order-linked payment: the money pays for an order, not for a wallet
        // top-up. Don't credit the user's balance — fulfilment will be handed
        // off to IFulfilmentService by WebhooksController right after this method.
        if (transaction.OrderId.HasValue)
        {
            _logger.LogInformation(
                "Order-linked payment completed: User {UserId}, Order {OrderId}, Amount {Amount} RUB, Txn {TransactionId} — fulfilment will follow",
                transaction.UserId, transaction.OrderId, transaction.Amount, transaction.TransactionId);
            return;
        }

        // Wallet top-up: credit the balance.
        user.Balance += transaction.Amount;
        _context.Transactions.Add(new Transaction
        {
            UserId = transaction.UserId,
            Amount = transaction.Amount,
            Type = TransactionType.Deposit,
            Description = $"Пополнение баланса [{transaction.TransactionId}]"
        });

        _logger.LogInformation(
            "Balance credited: {UserId} +{Amount} RUB, transaction: {TransactionId}",
            transaction.UserId, transaction.Amount, transaction.TransactionId);
    }

    // Старый метод для обратной совместимости
    public async Task<(bool Success, string? TransactionId, string? Error)> ProcessDepositAsync(
        string userId,
        decimal amount,
        string paymentMethod)
    {
        // Маппинг старого формата в новый
        var method = paymentMethod?.ToLower() switch
        {
            "card" => PaymentMethod.Card,
            "sbp" => PaymentMethod.SBP,
            "yoomoney" => PaymentMethod.YooMoney,
            "balance" => PaymentMethod.Balance,
            _ => PaymentMethod.Card
        };

        var result = await CreateDepositAsync(userId, amount, method);

        return (result.Success, result.TransactionId, result.ErrorMessage);
    }
}
