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
    /// <summary>Создать платеж для пополнения баланса</summary>
    Task<PaymentResult> CreateDepositAsync(string userId, decimal amount, PaymentMethod method, string? clientIp = null);

    /// <summary>Обработать webhook от провайдера</summary>
    Task<bool> ProcessWebhookAsync(string providerName, Dictionary<string, string> headers, string body);

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
        string? clientIp = null)
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

        // Получаем провайдер
        var provider = _providerFactory.GetProviderForMethod(method);
        if (provider == null)
        {
            _logger.LogWarning("No provider found for method {Method}", method);
            return PaymentResult.Failed("Метод оплаты временно недоступен");
        }

        // Создаем запрос
        var request = new PaymentRequest
        {
            UserId = userId,
            Amount = amount,
            Currency = "RUB",
            Method = method,
            Email = user.Email,
            Description = $"Пополнение баланса DigiVault",
            SuccessUrl = "/Account/Deposit?success=true",
            CancelUrl = "/Account/Deposit?cancelled=true",
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

    public async Task<bool> ProcessWebhookAsync(
        string providerName,
        Dictionary<string, string> headers,
        string body)
    {
        _logger.LogInformation("Processing webhook from {Provider}", providerName);

        var provider = _providerFactory.GetProvider(providerName);
        if (provider == null)
        {
            _logger.LogWarning("Unknown provider in webhook: {Provider}", providerName);
            return false;
        }

        var validationResult = await provider.ValidateWebhookAsync(headers, body);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "Webhook validation failed for {Provider}: {Error}",
                providerName, validationResult.ErrorMessage);
            return false;
        }

        if (string.IsNullOrEmpty(validationResult.TransactionId))
        {
            _logger.LogWarning("No transaction ID in webhook from {Provider}", providerName);
            return false;
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
            return false;
        }

        if (validationResult.NewStatus.HasValue)
        {
            transaction.Status = validationResult.NewStatus.Value;
            transaction.UpdatedAt = DateTime.UtcNow;

            if (validationResult.NewStatus == PaymentStatus.Completed)
            {
                transaction.CompletedAt = DateTime.UtcNow;
                await CompletePaymentInternalAsync(transaction);
            }

            await _context.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Webhook processed for {TransactionId}, new status: {Status}",
            transaction.TransactionId, transaction.Status);

        return true;
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
        // Зачисляем средства на баланс
        var user = await _context.Users.FindAsync(transaction.UserId);
        if (user == null)
        {
            _logger.LogError("User not found for payment: {UserId}", transaction.UserId);
            return;
        }

        user.Balance += transaction.Amount;

        // Создаем запись о транзакции баланса
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
