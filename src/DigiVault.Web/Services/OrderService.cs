using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Services.Fulfilment;
using DigiVault.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

public class PurchaseResult
{
    public bool Success { get; set; }
    public string? OrderNumber { get; set; }
    public string? ProductKey { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? NewBalance { get; set; }

    /// <summary>
    /// For external-payment purchases: where to redirect the user so they can
    /// pay on the provider's hosted page (Enot, YooKassa, etc.).
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>For external-payment purchases: our internal Order id.</summary>
    public int? OrderId { get; set; }
}

public interface IOrderService
{
    Task<OrderViewModel?> GetOrderAsync(string userId, int orderId);
    Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber);
    Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10);
    Task<List<TransactionViewModel>> GetTransactionsAsync(string userId, int count = 20);
    Task<PurchaseResult> CreatePurchaseAsync(string userId, int gameProductId, int quantity, string? deliveryInfo);

    /// <summary>
    /// Creates an Order in <see cref="OrderStatus.Pending"/> and asks the
    /// payment provider for a hosted-checkout URL. The user is redirected
    /// there; on successful webhook the order moves to Processing →
    /// Completed via the fulfilment pipeline.
    /// <paramref name="enotService"/> — optional Enot service code to lock
    /// the checkout to a single payment system (e.g. <c>"card"</c>, <c>"sbp"</c>).
    /// </summary>
    Task<PurchaseResult> CreateExternalPurchaseAsync(string userId, int gameProductId, int quantity,
        string? deliveryInfo, string paymentMethod, string? enotService, string siteBaseUrl, string? clientIp);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IBalanceService _balanceService;
    private readonly IFulfilmentService _fulfilment;
    private readonly DigiVault.Core.Interfaces.IPaymentProviderFactory _providerFactory;
    private readonly ILogger<OrderService> _logger;

    public OrderService(ApplicationDbContext context, IBalanceService balanceService,
        IFulfilmentService fulfilment, DigiVault.Core.Interfaces.IPaymentProviderFactory providerFactory,
        ILogger<OrderService> logger)
    {
        _context = context;
        _balanceService = balanceService;
        _fulfilment = fulfilment;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async Task<PurchaseResult> CreatePurchaseAsync(string userId, int gameProductId, int quantity, string? deliveryInfo)
    {
        try
        {
            // 1. Получить продукт
            var gameProduct = await _context.GameProducts
                .FirstOrDefaultAsync(gp => gp.Id == gameProductId && gp.IsActive);

            if (gameProduct == null)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар не найден или недоступен" };

            if (gameProduct.StockQuantity < quantity)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар временно недоступен (нет в наличии)" };

            var totalPrice = gameProduct.Price * quantity;

            // 2. Проверить баланс
            var balance = await _balanceService.GetBalanceAsync(userId);
            if (balance < totalPrice)
                return new PurchaseResult { Success = false, ErrorMessage = $"Недостаточно средств. Необходимо: {totalPrice:N0} ₽, на балансе: {balance:N0} ₽" };

            // 3. Списать средства
            var deductResult = await _balanceService.DeductFundsAsync(userId, totalPrice, $"Покупка: {gameProduct.Name}");
            if (!deductResult)
                return new PurchaseResult { Success = false, ErrorMessage = "Ошибка при списании средств" };

            // 4. Создать заказ в состоянии Processing — оплата прошла, ждём доставку.
            //    Финальный статус Completed выставляет IFulfilmentService.
            var orderNumber = GenerateOrderNumber();
            var order = new Order
            {
                UserId = userId,
                OrderNumber = orderNumber,
                TotalAmount = totalPrice,
                Status = OrderStatus.Processing,
                CreatedAt = DateTime.UtcNow,
                DeliveryInfo = deliveryInfo
            };
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // 5. Создать OrderItem (DeliveryStatus = Pending по умолчанию).
            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                GameProductId = gameProductId,
                Quantity = quantity,
                UnitPrice = gameProduct.Price,
                TotalPrice = totalPrice
            };
            _context.OrderItems.Add(orderItem);

            // 6. Транзакция в кошельке.
            var transaction = new Transaction
            {
                UserId = userId,
                Amount = totalPrice,
                Type = TransactionType.Purchase,
                Description = $"Покупка: {gameProduct.Name}",
                OrderId = order.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.Transactions.Add(transaction);

            // 7. Уменьшить остаток.
            gameProduct.StockQuantity -= quantity;
            await _context.SaveChangesAsync();

            // 8. Прогнать фулфилмент сразу — генерация payload синхронна и быстрая.
            //    Если упадёт по какой-то причине — фоновый OrderFulfilmentBackgroundService
            //    добьёт заказ в течение 30 секунд.
            await _fulfilment.DeliverOrderAsync(order.Id);

            var newBalance = await _balanceService.GetBalanceAsync(userId);

            _logger.LogInformation("Purchase completed: Order {OrderNumber}, User {UserId}, Product {ProductName}, Amount {Amount}",
                orderNumber, userId, gameProduct.Name, totalPrice);

            return new PurchaseResult
            {
                Success = true,
                OrderNumber = orderNumber,
                NewBalance = newBalance
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchase failed for user {UserId}, product {GameProductId}", userId, gameProductId);
            return new PurchaseResult { Success = false, ErrorMessage = "Произошла ошибка при оформлении заказа" };
        }
    }

    public async Task<PurchaseResult> CreateExternalPurchaseAsync(string userId, int gameProductId,
        int quantity, string? deliveryInfo, string paymentMethod, string? enotService,
        string siteBaseUrl, string? clientIp)
    {
        try
        {
            // 1. Validate product + stock
            var gameProduct = await _context.GameProducts
                .FirstOrDefaultAsync(gp => gp.Id == gameProductId && gp.IsActive);
            if (gameProduct == null)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар не найден или недоступен" };
            if (gameProduct.StockQuantity < quantity)
                return new PurchaseResult { Success = false, ErrorMessage = "Товар временно недоступен (нет в наличии)" };

            var totalPrice = gameProduct.Price * quantity;

            // 2. Resolve the provider that handles this method.
            var method = MapPaymentMethod(paymentMethod);
            var provider = _providerFactory.GetProviderForMethod(method);
            _logger.LogInformation(
                "External purchase: requested paymentMethod='{Raw}' → mapped to {Method}, picked provider '{Provider}' (enabled={Enabled})",
                paymentMethod, method, provider?.Name ?? "(none)", provider?.IsEnabled);
            if (provider == null || !provider.IsEnabled)
                return new PurchaseResult { Success = false, ErrorMessage = "Способ оплаты временно недоступен" };

            // 3. Create the Order in Pending — payment hasn't arrived yet.
            //    Stock is decremented up-front to avoid two simultaneous checkouts
            //    going through; if the user abandons the flow we let the order
            //    expire naturally (no auto-refund logic for now).
            var orderNumber = GenerateOrderNumber();
            var order = new Order
            {
                UserId = userId,
                OrderNumber = orderNumber,
                TotalAmount = totalPrice,
                Status = OrderStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                DeliveryInfo = deliveryInfo
            };
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                GameProductId = gameProductId,
                Quantity = quantity,
                UnitPrice = gameProduct.Price,
                TotalPrice = totalPrice
            };
            _context.OrderItems.Add(orderItem);
            gameProduct.StockQuantity -= quantity;
            await _context.SaveChangesAsync();

            // 4. Ask the provider to create a payment. We pass our future webhook,
            //    success and fail URLs — the user lands back here after Enot.
            var user = await _context.Users.FindAsync(userId);
            var siteBase = siteBaseUrl.TrimEnd('/');
            var metadata = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(enotService))
                metadata["enot_service"] = enotService.Trim();

            // Per top-level UI category (card / sbp / qr / p2p / crypto / wallet),
            // hand the gateway the EXACT list of services to expose. Without this
            // we'd fall back to PaymentMethod-based filtering — but our enum
            // doesn't have QR or P2P values, so they'd collapse into SBP/Card and
            // the user would see the wrong options on Enot's checkout page.
            var enotServices = MapPaymentMethodToEnotServices(paymentMethod);
            if (enotServices.Length > 0)
                metadata["enot_services"] = string.Join(",", enotServices);

            var paymentRequest = new DigiVault.Core.Models.Payment.PaymentRequest
            {
                UserId      = userId,
                Amount      = totalPrice,
                Currency    = "RUB",
                Method      = method,
                Email       = user?.Email,
                Description = $"Заказ {orderNumber}: {gameProduct.Name}",
                OrderId     = order.Id,
                SuccessUrl  = $"{siteBase}/Account/PaymentSuccess?orderId={order.Id}",
                CancelUrl   = $"{siteBase}/Account/PaymentFail?orderId={order.Id}",
                WebhookUrl  = $"{siteBase}/api/webhooks/{provider.Name}",
                ClientIp    = clientIp,
                Metadata    = metadata.Count > 0 ? metadata : null,
            };

            var providerResult = await provider.CreatePaymentAsync(paymentRequest);
            if (!providerResult.Success || string.IsNullOrEmpty(providerResult.RedirectUrl))
            {
                // Roll back the Order — provider rejected us.
                _context.OrderItems.Remove(orderItem);
                _context.Orders.Remove(order);
                gameProduct.StockQuantity += quantity;
                await _context.SaveChangesAsync();
                return new PurchaseResult { Success = false,
                    ErrorMessage = providerResult.ErrorMessage ?? "Не удалось создать платёж" };
            }

            // 5. Persist the PaymentTransaction so the webhook can find it.
            //    transaction.TransactionId == provider's order_id (what we control).
            //    transaction.ProviderTransactionId == provider's invoice id (what they return).
            _context.PaymentTransactions.Add(new PaymentTransaction
            {
                TransactionId         = providerResult.TransactionId ?? Guid.NewGuid().ToString("N"),
                ProviderTransactionId = providerResult.ProviderTransactionId,
                ProviderName          = provider.Name,
                UserId                = userId,
                OrderId               = order.Id,
                Method                = method,
                Status                = DigiVault.Core.Enums.PaymentStatus.Pending,
                Amount                = totalPrice,
                Currency              = "RUB",
                Description           = paymentRequest.Description,
                ClientIp              = clientIp,
                CreatedAt             = DateTime.UtcNow,
                UpdatedAt             = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "External purchase initiated: Order {OrderNumber}, Provider {Provider}, User {UserId}, Amount {Amount}",
                orderNumber, provider.Name, userId, totalPrice);

            return new PurchaseResult
            {
                Success     = true,
                OrderNumber = orderNumber,
                OrderId     = order.Id,
                RedirectUrl = providerResult.RedirectUrl,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External purchase failed for user {UserId}, product {GameProductId}",
                userId, gameProductId);
            return new PurchaseResult { Success = false, ErrorMessage = "Произошла ошибка при оформлении заказа" };
        }
    }

    private static DigiVault.Core.Enums.PaymentMethod MapPaymentMethod(string raw) => raw?.ToLowerInvariant() switch
    {
        "card"   => DigiVault.Core.Enums.PaymentMethod.Card,
        "sbp"    => DigiVault.Core.Enums.PaymentMethod.SBP,
        "qr"     => DigiVault.Core.Enums.PaymentMethod.SBP,    // routing only — exact services come from MapPaymentMethodToEnotServices
        "p2p"    => DigiVault.Core.Enums.PaymentMethod.Card,
        "crypto" => DigiVault.Core.Enums.PaymentMethod.Crypto,
        "wallet" => DigiVault.Core.Enums.PaymentMethod.YooMoney,
        _        => DigiVault.Core.Enums.PaymentMethod.Card,
    };

    /// <summary>
    /// Top-level UI category → list of Enot service codes actually enabled in
    /// our merchant cabinet (verified against
    /// <c>GET https://api.enot.io/shops/{shopId}/payment-tariffs</c>).
    /// Only services with <c>status=enabled</c> here are exposed to users.
    /// Re-check when tariffs change in the Enot cabinet.
    /// </summary>
    private static string[] MapPaymentMethodToEnotServices(string raw) => raw?.ToLowerInvariant() switch
    {
        "card"   => new[] { "card" },                         // includes Visa / MC / МИР inside this single Enot service
        "sbp"    => new[] { "sbp" },                          // SBP also renders the payment as a scannable QR
        "crypto" => new[] { "bitcoin", "usdt_trc20", "usdt_erc20", "ethereum", "litecoin", "dash", "trx", "xmr", "doge" },
        _        => Array.Empty<string>(),                    // qr / p2p / wallet etc. — not enabled in cabinet
    };

    public static string GenerateOrderNumber()
    {
        return $"DV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
    }

    public async Task<OrderViewModel?> GetOrderAsync(string userId, int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        return order != null ? MapToViewModel(order) : null;
    }

    public async Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == userId);

        return order != null ? MapToViewModel(order) : null;
    }

    public async Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10)
    {
        var query = _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.GameProduct)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt);

        var totalOrders = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalOrders / (double)pageSize);

        var orders = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new OrderHistoryViewModel
        {
            Orders = orders.Select(MapToViewModel).ToList(),
            CurrentPage = page,
            TotalPages = totalPages
        };
    }

    public async Task<List<TransactionViewModel>> GetTransactionsAsync(string userId, int count = 20)
    {
        var transactions = await _context.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(count)
            .ToListAsync();

        return transactions.Select(t => new TransactionViewModel
        {
            Id = t.Id,
            Amount = t.Amount,
            Type = t.Type,
            Description = t.Description,
            CreatedAt = t.CreatedAt
        }).ToList();
    }

    private static OrderViewModel MapToViewModel(Order order)
    {
        return new OrderViewModel
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            TotalAmount = order.TotalAmount,
            Status = order.Status,
            CreatedAt = order.CreatedAt,
            CompletedAt = order.CompletedAt,
            Items = order.OrderItems.Select(oi => new OrderItemViewModel
            {
                ProductId = oi.GameProductId,
                ProductName = oi.GameProduct?.Name ?? "Unknown",
                ImageUrl = oi.GameProduct?.ImageUrl,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                TotalPrice = oi.TotalPrice,
                DeliveryStatus = oi.DeliveryStatus,
                Delivery = DeliveryPayload.Deserialize(oi.DeliveryPayloadJson),
                ProductKeys = oi.ProductKeys.Select(k => k.KeyValue).ToList()
            }).ToList()
        };
    }
}
