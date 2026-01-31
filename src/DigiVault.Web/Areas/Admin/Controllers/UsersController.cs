using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class UsersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public UsersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
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

        TempData["SuccessMessage"] = $"Balance adjusted by ${amount:F2}";
        return RedirectToAction(nameof(Details), new { id });
    }
}
