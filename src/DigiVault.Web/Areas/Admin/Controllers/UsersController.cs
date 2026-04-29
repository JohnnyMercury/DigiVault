using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class UsersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOrderService _orderService;

    public UsersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
        IOrderService orderService)
    {
        _context = context;
        _userManager = userManager;
        _orderService = orderService;
    }

    public async Task<IActionResult> Index(string? search = null, int page = 1)
    {
        const int pageSize = 20;

        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Email!.Contains(search));

        var totalUsers = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalUsers / (double)pageSize);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = search;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;

        return View(users);
    }

    public async Task<IActionResult> Details(string id)
    {
        var user = await _context.Users
            .Include(u => u.Orders)
            .Include(u => u.Transactions)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        ViewBag.Roles = roles;

        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        user.IsActive = !user.IsActive;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = user.IsActive ? "User activated" : "User deactivated";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustBalance(string id, decimal amount, string reason)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        user.Balance += amount;

        _context.Transactions.Add(new Core.Entities.Transaction
        {
            UserId = id,
            Amount = amount,
            Type = amount > 0 ? Core.Enums.TransactionType.Deposit : Core.Enums.TransactionType.Refund,
            Description = $"Admin adjustment: {reason}"
        });

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Баланс изменён на {amount:N2} ₽";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Пользователь {user.Email} удалён";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Read-only "view as user" — shows the user's order history exactly as
    /// they would see it in their cabinet. No state-mutating actions are
    /// rendered (no buttons, no forms). Used by support / verification.
    /// </summary>
    public async Task<IActionResult> Cabinet(string id, int page = 1)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        var history = await _orderService.GetOrderHistoryAsync(id, page);
        var transactions = await _orderService.GetTransactionsAsync(id, count: 20);

        ViewBag.TargetUser = user;
        ViewBag.Transactions = transactions;
        return View(history);
    }

    /// <summary>
    /// Read-only view of one specific order belonging to <paramref name="id"/>,
    /// rendered in the same layout the user sees. The view re-uses the
    /// _DeliveryPayload partial so the admin sees identical credentials/cards.
    /// </summary>
    public async Task<IActionResult> CabinetOrder(string id, int orderId)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        var order = await _orderService.GetOrderAsync(id, orderId);
        if (order == null) return NotFound();

        ViewBag.TargetUser = user;
        return View(order);
    }
}
