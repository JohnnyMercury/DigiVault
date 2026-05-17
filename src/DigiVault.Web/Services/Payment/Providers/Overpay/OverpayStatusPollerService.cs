using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Services.Fulfilment;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.Overpay;

/// <summary>
/// Periodic safety-net poller for Overpay payments.
///
/// Overpay's webhook delivery has been silent in production (0 hits in
/// nginx logs over months) despite real customer payments — hence we
/// poll <c>GET /orders/{id}</c> via mTLS to recover stuck Pending
/// transactions.
///
/// Same throttling envelope as Enot/Pally pollers: 10-min grace, 5-min
/// per-txn cooldown, 2-hour absolute window.
/// </summary>
public class OverpayStatusPollerService : BackgroundService
{
    private static readonly TimeSpan SweepInterval  = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialGrace   = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PerTxnInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxPollWindow  = TimeSpan.FromHours(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OverpayStatusPollerService> _log;

    public OverpayStatusPollerService(
        IServiceScopeFactory scopeFactory,
        ILogger<OverpayStatusPollerService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "OverpayStatusPollerService started; sweep every {Sweep}, per-txn min {PerTxn}, max window {Max}",
            SweepInterval, PerTxnInterval, MaxPollWindow);

        try { await Task.Delay(SweepInterval, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "OverpayStatusPollerService.Sweep threw — will retry");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("OverpayStatusPollerService stopping");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db         = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var providers  = scope.ServiceProvider.GetServices<DigiVault.Core.Interfaces.IPaymentProvider>();
        var fulfilment = scope.ServiceProvider.GetRequiredService<IFulfilmentService>();

        var overpay = providers.OfType<OverpayPaymentProvider>().FirstOrDefault();
        if (overpay == null) return;

        var now       = DateTime.UtcNow;
        var ageCutoff = now - InitialGrace;
        var staleness = now - PerTxnInterval;
        var maxAge    = now - MaxPollWindow;

        var candidates = await db.PaymentTransactions
            .Where(t => t.ProviderName == "overpay"
                     && t.Status       == PaymentStatus.Pending
                     && t.CreatedAt   <= ageCutoff
                     && t.CreatedAt   >= maxAge
                     && t.UpdatedAt   <= staleness
                     && !string.IsNullOrEmpty(t.ProviderTransactionId))
            .OrderBy(t => t.UpdatedAt)
            .Take(25)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _log.LogInformation("Overpay poll sweep: {Count} txn(s) due", candidates.Count);

        foreach (var tx in candidates)
        {
            if (ct.IsCancellationRequested) break;
            await PollOneAsync(db, overpay, fulfilment, tx, ct);
        }
    }

    private async Task PollOneAsync(
        ApplicationDbContext db,
        OverpayPaymentProvider provider,
        IFulfilmentService fulfilment,
        PaymentTransaction tx,
        CancellationToken ct)
    {
        var result = await provider.GetPaymentStatusAsync(tx.TransactionId, ct);

        tx.UpdatedAt = DateTime.UtcNow;

        switch (result.Status)
        {
            case PaymentStatus.Completed:
                _log.LogInformation(
                    "Overpay poll: txn={Txn} → Completed (amount={Amount})",
                    tx.TransactionId, result.Amount);

                tx.Status      = PaymentStatus.Completed;
                tx.CompletedAt = DateTime.UtcNow;
                if (result.Amount > 0) tx.Amount = result.Amount;

                if (tx.OrderId.HasValue)
                {
                    var order = await db.Orders.FindAsync(new object?[] { tx.OrderId.Value }, ct);
                    if (order != null && order.Status == OrderStatus.Pending)
                        order.Status = OrderStatus.Processing;
                }
                else
                {
                    await CreditWalletAsync(db, tx, "Overpay", ct);
                }

                await db.SaveChangesAsync(ct);

                if (tx.OrderId.HasValue)
                {
                    try { await fulfilment.DeliverOrderAsync(tx.OrderId.Value, ct); }
                    catch (Exception ex)
                    {
                        _log.LogError(ex,
                            "Overpay poll: fulfilment for order {OrderId} (txn {Txn}) failed; sweeper will retry",
                            tx.OrderId, tx.TransactionId);
                    }
                }
                break;

            case PaymentStatus.Failed:
            case PaymentStatus.Cancelled:
            case PaymentStatus.Expired:
                tx.Status = result.Status;
                tx.ErrorMessage = result.Message;
                await db.SaveChangesAsync(ct);
                _log.LogInformation(
                    "Overpay poll: txn={Txn} → {Status}",
                    tx.TransactionId, result.Status);
                break;

            case PaymentStatus.Pending:
            default:
                await db.SaveChangesAsync(ct);
                break;
        }
    }

    private async Task CreditWalletAsync(
        ApplicationDbContext db, PaymentTransaction tx, string providerLabel, CancellationToken ct)
    {
        var user = await db.Users.FindAsync(new object?[] { tx.UserId }, ct);
        if (user == null)
        {
            _log.LogError("Overpay poll: user not found for txn {Txn} (UserId={UserId})",
                tx.TransactionId, tx.UserId);
            return;
        }

        var alreadyCredited = await db.Transactions
            .AnyAsync(t => t.Description != null && t.Description.Contains(tx.TransactionId), ct);
        if (alreadyCredited)
        {
            _log.LogInformation("Overpay poll: txn {Txn} already credited — skipping", tx.TransactionId);
            return;
        }

        user.Balance += tx.Amount;

        db.Transactions.Add(new Transaction
        {
            UserId      = tx.UserId,
            Amount      = tx.Amount,
            Type        = TransactionType.Deposit,
            Description = $"Пополнение баланса [{tx.TransactionId}]",
            CreatedAt   = DateTime.UtcNow,
        });

        db.WalletTransactions.Add(new WalletTransaction
        {
            UserId                  = tx.UserId,
            Amount                  = tx.Amount,
            BalanceAfterTransaction = user.Balance,
            Type                    = WalletTransactionType.Deposit,
            Description             = $"Пополнение через {providerLabel} (авто-сверка)",
            Reference               = tx.TransactionId,
            Status                  = WalletTransactionStatus.Completed,
            ProcessedAt             = DateTime.UtcNow,
        });

        _log.LogInformation(
            "Overpay poll: credited user {UserId} +{Amount} RUB (txn {Txn}); new balance {Balance}",
            tx.UserId, tx.Amount, tx.TransactionId, user.Balance);
    }
}
