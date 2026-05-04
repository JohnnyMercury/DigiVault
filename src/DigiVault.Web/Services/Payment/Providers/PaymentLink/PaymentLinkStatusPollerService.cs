using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Services.Fulfilment;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Payment.Providers.PaymentLink;

/// <summary>
/// Background poller that asks PaymentLink for the status of every Pending
/// transaction we own. Required by their integration spec § 4.6:
///
///   • PaymentLink only sends a webhook on success — wait→error transitions
///     and "abandoned by customer" cases never trigger a callback.
///
/// Throttling rules (per support guidance):
///   • Don't poll a transaction in the first 10 minutes after creation —
///     give the synchronous webhook a chance to land first.
///   • Poll no more than once per 10 minutes per transaction.
///   • Stop polling 2 hours after creation if status is still "wait" —
///     such payments are considered abandoned.
///   • Stop on a final OK / error response too, obviously.
///
/// We rate-limit per-txn via PaymentTransaction.UpdatedAt, which we touch on
/// every poll regardless of result.
/// </summary>
public class PaymentLinkStatusPollerService : BackgroundService
{
    // Sweep cadence — hits the DB cheaply, the per-txn 10-min cap is what
    // actually limits outbound /operate calls. 60s gives us snappy reaction
    // when transactions transition between buckets.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    // Don't bother PaymentLink in the first window after creation — their
    // own webhook usually beats us to the punch. Per spec: 10 min.
    private static readonly TimeSpan InitialGrace = TimeSpan.FromMinutes(10);

    // Minimum interval between two /operate calls for the same txn.
    private static readonly TimeSpan PerTxnInterval = TimeSpan.FromMinutes(10);

    // After this much time we declare the payment abandoned and stop polling.
    private static readonly TimeSpan MaxPollWindow = TimeSpan.FromHours(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentLinkStatusPollerService> _log;

    public PaymentLinkStatusPollerService(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentLinkStatusPollerService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation(
            "PaymentLinkStatusPollerService started; sweep every {Sweep}, per-txn min {PerTxn}, max window {Max}",
            SweepInterval, PerTxnInterval, MaxPollWindow);

        // Wait one full sweep before the first run — gives the host time to
        // finish bootstrapping and avoids double-polling on app restart.
        try { await Task.Delay(SweepInterval, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PaymentLinkStatusPollerService.Sweep threw — will retry");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("PaymentLinkStatusPollerService stopping");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var providers = scope.ServiceProvider.GetServices<DigiVault.Core.Interfaces.IPaymentProvider>();
        var fulfilment = scope.ServiceProvider.GetRequiredService<IFulfilmentService>();

        var paymentlink = providers.OfType<PaymentLinkPaymentProvider>().FirstOrDefault();
        if (paymentlink == null) return;

        var now = DateTime.UtcNow;
        var ageCutoff = now - InitialGrace;
        var staleness = now - PerTxnInterval;
        var maxAge = now - MaxPollWindow;

        var candidates = await db.PaymentTransactions
            .Where(t => t.ProviderName == "paymentlink"
                     && t.Status == PaymentStatus.Pending
                     && t.CreatedAt <= ageCutoff       // grace period passed
                     && t.CreatedAt >= maxAge          // not abandoned yet
                     && t.UpdatedAt <= staleness       // not polled in last interval
                     && !string.IsNullOrEmpty(t.ProviderTransactionId))
            .OrderBy(t => t.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _log.LogInformation("PaymentLink poll sweep: {Count} txn(s) due", candidates.Count);

        foreach (var tx in candidates)
        {
            if (ct.IsCancellationRequested) break;
            await PollOneAsync(db, paymentlink, fulfilment, tx, ct);
        }
    }

    private async Task PollOneAsync(
        ApplicationDbContext db,
        PaymentLinkPaymentProvider provider,
        IFulfilmentService fulfilment,
        PaymentTransaction tx,
        CancellationToken ct)
    {
        var result = await provider.PollOperateStatusAsync(tx.ProviderTransactionId!, ct);

        // Touch UpdatedAt regardless of outcome so the per-txn rate limit holds.
        tx.UpdatedAt = DateTime.UtcNow;

        var statusLower = (result.Status ?? "").ToLowerInvariant();
        switch (statusLower)
        {
            case "ok":
            case "authorise":
            case "unblocked":
                _log.LogInformation(
                    "PaymentLink poll: txn={Txn} → Completed (status={Status} finalAmount={Amount})",
                    tx.TransactionId, statusLower, result.FinalAmount);
                tx.Status = PaymentStatus.Completed;
                tx.CompletedAt = DateTime.UtcNow;
                if (result.FinalAmount > 0) tx.Amount = result.FinalAmount;
                if (tx.OrderId.HasValue)
                {
                    var order = await db.Orders.FindAsync(new object?[] { tx.OrderId.Value }, ct);
                    if (order != null && order.Status == OrderStatus.Pending)
                        order.Status = OrderStatus.Processing;
                }
                await db.SaveChangesAsync(ct);
                if (tx.OrderId.HasValue)
                    await fulfilment.DeliverOrderAsync(tx.OrderId.Value, ct);
                break;

            case "reversal":
                tx.Status = PaymentStatus.Refunded;
                await db.SaveChangesAsync(ct);
                _log.LogInformation("PaymentLink poll: txn={Txn} → Refunded", tx.TransactionId);
                break;

            case "error":
                tx.Status = PaymentStatus.Failed;
                tx.ErrorMessage = string.IsNullOrEmpty(result.ErrorText)
                    ? $"PSP error code {result.ErrorCode}"
                    : result.ErrorText;
                await db.SaveChangesAsync(ct);
                _log.LogInformation(
                    "PaymentLink poll: txn={Txn} → Failed ({Code} {Text})",
                    tx.TransactionId, result.ErrorCode, result.ErrorText);
                break;

            case "wait":
                // Still in flight — just persist UpdatedAt so we throttle.
                await db.SaveChangesAsync(ct);
                _log.LogDebug("PaymentLink poll: txn={Txn} still wait", tx.TransactionId);
                break;

            default:
                // Unknown status — log and persist UpdatedAt to throttle.
                _log.LogWarning(
                    "PaymentLink poll: txn={Txn} unknown status='{Status}' — keeping Pending",
                    tx.TransactionId, statusLower);
                await db.SaveChangesAsync(ct);
                break;
        }
    }
}
