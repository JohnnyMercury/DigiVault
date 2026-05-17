using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Services.Fulfilment;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.Enot;

/// <summary>
/// Periodic safety-net poller for Enot.io payments.
///
/// Enot.io ships webhooks signed with HMAC-SHA256 (header
/// <c>x-api-sha256-signature</c>) — but in practice they can be lost or
/// rejected (rotated «Дополнительный ключ», network blip, transient bug).
/// Without a poll fallback the only recourse is admin-side manual completion,
/// which means every missed callback turns into a customer complaint and a
/// 5-minute DB-edit chore.
///
/// This service mirrors the pattern from
/// <see cref="PaymentLink.PaymentLinkStatusPollerService"/>: every minute,
/// take a small batch of Pending Enot transactions older than the grace
/// window, query <c>/invoice/info</c>, and apply the same status-update
/// + balance-credit + fulfilment-trigger pipeline the webhook would.
///
/// Throttling:
///   • 10-minute initial grace — give the webhook a chance to land first.
///   • 5-minute per-txn cooldown via <c>UpdatedAt</c>.
///   • 2-hour absolute window — older Pending tx are abandoned (Enot's
///     <c>expire_at</c> is typically 5 h, but customers paying after the
///     2-hour mark are rare and admin-recoverable).
/// </summary>
public class EnotStatusPollerService : BackgroundService
{
    private static readonly TimeSpan SweepInterval  = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialGrace   = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PerTxnInterval = TimeSpan.FromMinutes(5);
    // 7-day window is intentionally wide — Enot's invoice itself only expires
    // after ~5 h, but we've previously seen weeks of paid-but-uncredited tx
    // accumulate when the webhook silently broke. Wide window + 5-min per-txn
    // throttle still bounds outbound load (≤ 12 calls/hr/tx).
    private static readonly TimeSpan MaxPollWindow  = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnotStatusPollerService> _log;

    public EnotStatusPollerService(
        IServiceScopeFactory scopeFactory,
        ILogger<EnotStatusPollerService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "EnotStatusPollerService started; sweep every {Sweep}, per-txn min {PerTxn}, max window {Max}",
            SweepInterval, PerTxnInterval, MaxPollWindow);

        // One full sweep delay before the first run — lets the app finish
        // bootstrapping and avoids a thundering-herd poll on startup.
        try { await Task.Delay(SweepInterval, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "EnotStatusPollerService.Sweep threw — will retry");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("EnotStatusPollerService stopping");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db         = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var providers  = scope.ServiceProvider.GetServices<DigiVault.Core.Interfaces.IPaymentProvider>();
        var fulfilment = scope.ServiceProvider.GetRequiredService<IFulfilmentService>();

        var enot = providers.OfType<EnotPaymentProvider>().FirstOrDefault();
        if (enot == null) return;

        var now       = DateTime.UtcNow;
        var ageCutoff = now - InitialGrace;
        var staleness = now - PerTxnInterval;
        var maxAge    = now - MaxPollWindow;

        var candidates = await db.PaymentTransactions
            .Where(t => t.ProviderName == "enot"
                     && t.Status       == PaymentStatus.Pending
                     && t.CreatedAt   <= ageCutoff
                     && t.CreatedAt   >= maxAge
                     && t.UpdatedAt   <= staleness)
            .OrderBy(t => t.UpdatedAt)
            .Take(25)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _log.LogInformation("Enot poll sweep: {Count} txn(s) due", candidates.Count);

        foreach (var tx in candidates)
        {
            if (ct.IsCancellationRequested) break;
            await PollOneAsync(db, enot, fulfilment, tx, ct);
        }
    }

    private async Task PollOneAsync(
        ApplicationDbContext db,
        EnotPaymentProvider provider,
        IFulfilmentService fulfilment,
        PaymentTransaction tx,
        CancellationToken ct)
    {
        var result = await provider.GetPaymentStatusAsync(tx.TransactionId, ct);

        // Touch UpdatedAt regardless of outcome to enforce the rate limit.
        tx.UpdatedAt = DateTime.UtcNow;

        switch (result.Status)
        {
            case PaymentStatus.Completed:
                _log.LogInformation(
                    "Enot poll: txn={Txn} → Completed (amount={Amount})",
                    tx.TransactionId, result.Amount);

                tx.Status      = PaymentStatus.Completed;
                tx.CompletedAt = DateTime.UtcNow;
                if (result.Amount > 0) tx.Amount = result.Amount;

                // Order-linked payment: flip status so the fulfilment sweeper
                // picks it up. Wallet top-up: credit the balance + log history.
                if (tx.OrderId.HasValue)
                {
                    var order = await db.Orders.FindAsync(new object?[] { tx.OrderId.Value }, ct);
                    if (order != null && order.Status == OrderStatus.Pending)
                        order.Status = OrderStatus.Processing;
                }
                else
                {
                    await CreditWalletAsync(db, tx, ct);
                }

                await db.SaveChangesAsync(ct);

                if (tx.OrderId.HasValue)
                {
                    try { await fulfilment.DeliverOrderAsync(tx.OrderId.Value, ct); }
                    catch (Exception ex)
                    {
                        _log.LogError(ex,
                            "Enot poll: fulfilment for order {OrderId} (txn {Txn}) failed; sweeper will retry",
                            tx.OrderId, tx.TransactionId);
                    }
                }
                break;

            case PaymentStatus.Expired:
            case PaymentStatus.Failed:
                tx.Status = result.Status;
                tx.ErrorMessage = result.Message;
                await db.SaveChangesAsync(ct);
                _log.LogInformation(
                    "Enot poll: txn={Txn} → {Status}",
                    tx.TransactionId, result.Status);
                break;

            case PaymentStatus.Refunded:
                tx.Status = PaymentStatus.Refunded;
                await db.SaveChangesAsync(ct);
                _log.LogInformation("Enot poll: txn={Txn} → Refunded", tx.TransactionId);
                break;

            case PaymentStatus.Pending:
            default:
                // Still in flight — persist UpdatedAt only.
                await db.SaveChangesAsync(ct);
                break;
        }
    }

    /// <summary>
    /// Credit a wallet-top-up Enot payment: updates user balance, writes the
    /// legacy Transaction row (used by old reports) AND a WalletTransaction
    /// row (rendered in Account → История пополнений). Both must stay in
    /// sync — without the WalletTransaction the customer sees the balance
    /// rise but no proof in their wallet history and assumes the deposit
    /// failed.
    /// </summary>
    private async Task CreditWalletAsync(ApplicationDbContext db, PaymentTransaction tx, CancellationToken ct)
    {
        var user = await db.Users.FindAsync(new object?[] { tx.UserId }, ct);
        if (user == null)
        {
            _log.LogError("Enot poll: user not found for txn {Txn} (UserId={UserId})",
                tx.TransactionId, tx.UserId);
            return;
        }

        // Idempotency: if a Transaction row referencing this transactionId
        // already exists we've already credited — bail. Belt-and-braces in
        // case the webhook AND the poller both fire on the same txn.
        var alreadyCredited = await db.Transactions
            .AnyAsync(t => t.Description != null
                        && t.Description.Contains(tx.TransactionId), ct);
        if (alreadyCredited)
        {
            _log.LogInformation("Enot poll: txn {Txn} already credited — skipping wallet update",
                tx.TransactionId);
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
            Description             = "Пополнение через Enot (авто-сверка)",
            Reference               = tx.TransactionId,
            Status                  = WalletTransactionStatus.Completed,
            ProcessedAt             = DateTime.UtcNow,
        });

        _log.LogInformation(
            "Enot poll: credited user {UserId} +{Amount} RUB (txn {Txn}); new balance {Balance}",
            tx.UserId, tx.Amount, tx.TransactionId, user.Balance);
    }
}
