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
}

public interface IOrderService
{
    Task<OrderViewModel?> GetOrderAsync(string userId, int orderId);
    Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber);
    Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10);
    Task<List<TransactionViewModel>> GetTransactionsAsync(string userId, int count = 20);
    Task<PurchaseResult> CreatePurchaseAsync(string userId, int gameProductId, int quantity, string? deliveryInfo);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IBalanceService _balanceService;
    private readonly IFulfilmentService _fulfilment;
    private readonly ILogger<OrderService> _logger;

    public OrderService(ApplicationDbContext context, IBalanceService balanceService,
        IFulfilmentService fulfilment, ILogger<OrderService> logger)
    {
        _context = context;
        _balanceService = balanceService;
        _fulfilment = fulfilment;
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
