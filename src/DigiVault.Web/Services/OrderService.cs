using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Services;

public interface IOrderService
{
    Task<OrderViewModel?> GetOrderAsync(string userId, int orderId);
    Task<OrderViewModel?> GetOrderByNumberAsync(string userId, string orderNumber);
    Task<OrderHistoryViewModel> GetOrderHistoryAsync(string userId, int page = 1, int pageSize = 10);
    Task<List<TransactionViewModel>> GetTransactionsAsync(string userId, int count = 20);
}

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;

    public OrderService(ApplicationDbContext context)
    {
        _context = context;
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
