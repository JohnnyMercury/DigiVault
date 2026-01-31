using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class OrdersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public OrdersController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(OrderStatus? status = null, string? search = null, int page = 1)
    {
        const int pageSize = 20;

        var query = _context.Orders
            .Include(o => o.User)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.OrderNumber.Contains(search) ||
                                    o.User.Email!.Contains(search));

        var totalOrders = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalOrders / (double)pageSize);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Status = status;
        ViewBag.Search = search;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;

        return View(orders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.Orders
            .Include(o => o.User)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
            return NotFound();

        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, OrderStatus status)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null)
            return NotFound();

        order.Status = status;
        if (status == OrderStatus.Completed)
            order.CompletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Order status updated";
        return RedirectToAction(nameof(Details), new { id });
    }
}
