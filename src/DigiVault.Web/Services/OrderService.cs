using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

public interface IOrderService
{
    Task<(bool Success, string? OrderNumber, string? Error)> CreateOrderAsync(string userId);
    Task<OrderViewModel?> GetOrderAsync(string userId, int orderId);
    Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber);
    Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10);
    Task<List<TransactionViewModel>> GetTransactionsAsync(string userId, int count = 20);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly ICartService _cartService;

    public OrderService(ApplicationDbContext context, ICartService cartService)
    {
        _context = context;
        _cartService = cartService;
    }

    public async Task<(bool Success, string? OrderNumber, string? Error)> CreateOrderAsync(string userId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return (false, null, "User not found");

            var cart = await _cartService.GetCartAsync(userId);
            if (cart.IsEmpty)
                return (false, null, "Cart is empty");

            if (user.Balance < cart.Total)
                return (false, null, "Insufficient balance");

            // Check stock availability
            foreach (var item in cart.Items)
            {
                var availableKeys = await _context.ProductKeys
                    .Where(k => k.ProductId == item.ProductId && !k.IsUsed)
                    .CountAsync();

                if (availableKeys < item.Quantity)
                    return (false, null, $"Not enough stock for {item.ProductName}");
            }

            // Create order
            var orderNumber = $"DV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";
            var order = new Order
            {
                UserId = userId,
                OrderNumber = orderNumber,
                TotalAmount = cart.Total,
                Status = OrderStatus.Processing
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Create order items and assign keys
            foreach (var item in cart.Items)
            {
                var orderItem = new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalPrice = item.TotalPrice
                };

                _context.OrderItems.Add(orderItem);
                await _context.SaveChangesAsync();

                // Assign product keys
                var keys = await _context.ProductKeys
                    .Where(k => k.ProductId == item.ProductId && !k.IsUsed)
                    .Take(item.Quantity)
                    .ToListAsync();

                foreach (var key in keys)
                {
                    key.IsUsed = true;
                    key.OrderItemId = orderItem.Id;
                    key.UsedAt = DateTime.UtcNow;
                }

                // Update stock
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product != null)
                    product.StockQuantity -= item.Quantity;
            }

            // Deduct balance
            user.Balance -= cart.Total;

            // Create transaction record
            _context.Transactions.Add(new Transaction
            {
                UserId = userId,
                Amount = -cart.Total,
                Type = TransactionType.Purchase,
                Description = $"Order {orderNumber}",
                OrderId = order.Id
            });

            // Complete order
            order.Status = OrderStatus.Completed;
            order.CompletedAt = DateTime.UtcNow;

            // Clear cart
            await _cartService.ClearCartAsync(userId);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, orderNumber, null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return (false, null, $"An error occurred: {ex.Message}");
        }
    }

    public async Task<OrderViewModel?> GetOrderAsync(string userId, int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        return order != null ? MapToViewModel(order) : null;
    }

    public async Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == userId);

        return order != null ? MapToViewModel(order) : null;
    }

    public async Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10)
    {
        var query = _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
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
                ProductId = oi.ProductId,
                ProductName = oi.Product.Name,
                ImageUrl = oi.Product.ImageUrl,
                Quantity = oi.Quantity,
                UnitPrice = oi.UnitPrice,
                TotalPrice = oi.TotalPrice,
                ProductKeys = oi.ProductKeys.Select(k => k.KeyValue).ToList()
            }).ToList()
        };
    }
}
