using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class DashboardController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public DashboardController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.Date;
        var thisMonth = new DateTime(today.Year, today.Month, 1);

        ViewBag.TotalUsers = await _context.Users.CountAsync();
        ViewBag.TotalProducts = await _context.Products.CountAsync();
        ViewBag.TotalOrders = await _context.Orders.CountAsync();
        ViewBag.TodayOrders = await _context.Orders.CountAsync(o => o.CreatedAt.Date == today);

        ViewBag.TotalRevenue = await _context.Orders
            .Where(o => o.Status == OrderStatus.Completed)
            .SumAsync(o => o.TotalAmount);

        ViewBag.MonthlyRevenue = await _context.Orders
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= thisMonth)
            .SumAsync(o => o.TotalAmount);

        ViewBag.RecentOrders = await _context.Orders
            .Include(o => o.User)
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .ToListAsync();

        ViewBag.LowStockProducts = await _context.Products
            .Where(p => p.IsActive && p.StockQuantity < 10)
            .OrderBy(p => p.StockQuantity)
            .Take(5)
            .ToListAsync();

        return View();
    }
}
