using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services.Fulfilment;

/// <summary>
/// Universal post-payment fulfilment. Generates delivery payloads for all
/// undelivered items in an order, then transitions the Order to
/// <see cref="OrderStatus.Completed"/> once every item is delivered.
///
/// Idempotent — calling <see cref="DeliverOrderAsync"/> repeatedly is safe and
/// only operates on items still in <see cref="DeliveryStatus.Pending"/>.
///
/// Wiring:
///   - Wallet purchase → <see cref="OrderService"/> calls this in-process
///     immediately after debit. Customer sees Completed instantly.
///   - External payment → <see cref="WebhooksController"/> calls this when
///     a successful payment webhook arrives (Order.Status: Pending → Processing).
///   - Safety net → <see cref="OrderFulfilmentBackgroundService"/> sweeps every
///     30 seconds and delivers any still-Pending items (handles app restarts,
///     transient generator failures, etc.).
/// </summary>
public interface IFulfilmentService
{
    Task<bool> DeliverOrderAsync(int orderId, CancellationToken ct = default);
    Task<int> SweepPendingAsync(int maxOrders = 50, CancellationToken ct = default);
}

public class FulfilmentService : IFulfilmentService
{
    private readonly ApplicationDbContext _db;
    private readonly ICredentialGenerator _generator;
    private readonly ILogger<FulfilmentService> _log;

    public FulfilmentService(ApplicationDbContext db, ICredentialGenerator generator,
        ILogger<FulfilmentService> log)
    {
        _db = db;
        _generator = generator;
        _log = log;
    }

    public async Task<bool> DeliverOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct).ThenInclude(p => p!.Game)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct).ThenInclude(p => p!.GiftCard)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct).ThenInclude(p => p!.VpnProvider)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null)
        {
            _log.LogWarning("DeliverOrderAsync: order {OrderId} not found", orderId);
            return false;
        }

        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Refunded)
        {
            _log.LogInformation("DeliverOrderAsync: order {OrderId} is in terminal state {Status}, skipping",
                orderId, order.Status);
            return false;
        }

        var pendingItems = order.OrderItems.Where(oi => oi.DeliveryStatus == DeliveryStatus.Pending).ToList();
        if (pendingItems.Count == 0)
        {
            // Already fully delivered — just make sure Order.Status reflects it.
            EnsureOrderCompleted(order);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        foreach (var item in pendingItems)
        {
            try
            {
                item.DeliveredAt = DateTime.UtcNow;
                var payload = _generator.Generate(item);
                item.DeliveryPayloadJson = payload.Serialize();
                item.DeliveryStatus = DeliveryStatus.Delivered;
            }
            catch (Exception ex)
            {
                item.DeliveryStatus = DeliveryStatus.Failed;
                _log.LogError(ex,
                    "Fulfilment failed for OrderItem {ItemId} (Order {OrderId}, Product {ProductId})",
                    item.Id, order.Id, item.GameProductId);
            }
        }

        EnsureOrderCompleted(order);
        await _db.SaveChangesAsync(ct);

        var delivered = pendingItems.Count(i => i.DeliveryStatus == DeliveryStatus.Delivered);
        _log.LogInformation("Order {OrderNumber}: delivered {Delivered}/{Total} items, order status now {Status}",
            order.OrderNumber, delivered, pendingItems.Count, order.Status);

        return delivered == pendingItems.Count;
    }

    public async Task<int> SweepPendingAsync(int maxOrders = 50, CancellationToken ct = default)
    {
        // Pick orders that have at least one Pending item and aren't terminal/Completed.
        var orderIds = await _db.Orders
            .Where(o => o.Status != OrderStatus.Completed
                     && o.Status != OrderStatus.Cancelled
                     && o.Status != OrderStatus.Refunded
                     && o.OrderItems.Any(oi => oi.DeliveryStatus == DeliveryStatus.Pending))
            .OrderBy(o => o.CreatedAt)
            .Take(maxOrders)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var swept = 0;
        foreach (var id in orderIds)
        {
            if (ct.IsCancellationRequested) break;
            if (await DeliverOrderAsync(id, ct)) swept++;
        }

        if (swept > 0)
            _log.LogInformation("FulfilmentService.Sweep: processed {Count} order(s)", swept);

        return swept;
    }

    /// <summary>If every item is now Delivered, flip the Order to Completed.</summary>
    private static void EnsureOrderCompleted(Order order)
    {
        if (order.Status == OrderStatus.Completed) return;

        var allDelivered = order.OrderItems.All(oi => oi.DeliveryStatus == DeliveryStatus.Delivered);
        if (allDelivered && order.OrderItems.Count > 0)
        {
            order.Status = OrderStatus.Completed;
            order.CompletedAt ??= DateTime.UtcNow;
        }
        else if (order.Status == OrderStatus.Pending)
        {
            // Payment confirmed (caller knows this) but we have at least some Pending or
            // Failed items left — bump to Processing so the next sweep picks it up.
            order.Status = OrderStatus.Processing;
        }
    }
}
