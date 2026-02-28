using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Models;
using DigiVault.Web.Services;
using DigiVault.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _context;
    private readonly IPaymentService _paymentService;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext context,
        IPaymentService paymentService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
        _paymentService = paymentService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _signInManager.PasswordSignInAsync(
            model.Email,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user != null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ViewBag.Error = "Аккаунт заблокирован. Попробуйте позже.";
            return View(model);
        }

        ViewBag.Error = "Неверный email или пароль";
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (model.Password != model.ConfirmPassword)
        {
            ViewBag.Errors = new[] { "Пароли не совпадают" };
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.UserName,
            Email = model.Email,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, "User");
            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Home");
        }

        ViewBag.Errors = result.Errors.Select(e => e.Description).ToArray();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var orders = await _context.Orders
            .Where(o => o.UserId == user.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var model = new DashboardViewModel
        {
            User = user,
            RecentOrders = orders.Take(5),
            TotalOrders = orders.Count,
            CompletedOrders = orders.Count(o => o.Status == OrderStatus.Completed),
            TotalSpent = orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalAmount),
            TotalKeys = await _context.ProductKeys.CountAsync(k => k.OrderItem != null && k.OrderItem.Order.UserId == user.Id && k.IsUsed)
        };

        return View(model);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Orders(int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var orders = await _context.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Product)
            .Where(o => o.UserId == user.Id)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var model = new OrderHistoryViewModel
        {
            Orders = orders.Select(o => new OrderViewModel
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                TotalAmount = o.TotalAmount,
                Status = o.Status,
                CreatedAt = o.CreatedAt,
                CompletedAt = o.CompletedAt,
                Items = o.OrderItems.Select(oi => new OrderItemViewModel
                {
                    ProductId = oi.ProductId,
                    ProductName = oi.Product?.Name ?? "Unknown",
                    ImageUrl = oi.Product?.ImageUrl,
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    TotalPrice = oi.TotalPrice
                }).ToList()
            }).ToList(),
            CurrentPage = page,
            TotalPages = 1
        };

        return View(model);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Deposit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var model = new DepositViewModel
        {
            CurrentBalance = user.Balance
        };

        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deposit(DepositViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        model.CurrentBalance = user.Balance;

        if (model.Amount <= 0)
        {
            ModelState.AddModelError("Amount", "Сумма должна быть больше нуля");
            return View(model);
        }

        // Stub: просто добавляем баланс
        user.Balance += model.Amount;
        await _userManager.UpdateAsync(user);

        // Записываем транзакцию
        var transaction = new Transaction
        {
            UserId = user.Id,
            Amount = model.Amount,
            Type = TransactionType.Deposit,
            Description = "Пополнение баланса",
            CreatedAt = DateTime.UtcNow
        };
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Баланс успешно пополнен на {model.Amount:N0} ₽";
        return RedirectToAction("Dashboard");
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Balance()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        var transactions = await _context.Set<WalletTransaction>()
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .ToListAsync();

        ViewBag.Balance = user.Balance;
        ViewBag.Transactions = transactions;
        return View();
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> WalletHistory(int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        const int pageSize = 20;
        var query = _context.Set<WalletTransaction>()
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync();
        var transactions = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Balance = user.Balance;
        ViewBag.Transactions = transactions;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        ViewBag.TotalCount = total;
        return View();
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        var orders = await _context.Orders
            .Where(o => o.UserId == user.Id)
            .ToListAsync();

        var model = new DigiVault.Web.ViewModels.ProfileViewModel
        {
            Email = user.Email ?? "",
            Balance = user.Balance,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            TotalOrders = orders.Count,
            TotalSpent = orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalAmount)
        };

        return View(model);
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View(new DigiVault.Web.ViewModels.ChangePasswordViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(DigiVault.Web.ViewModels.ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
            TempData["Success"] = "Пароль успешно изменён";
            return RedirectToAction("Profile");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);

        return View(model);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Transactions(int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        const int pageSize = 20;
        var query = _context.Transactions
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt);

        var total = await query.CountAsync();
        var transactions = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Balance = user.Balance;
        ViewBag.Transactions = transactions;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        return View();
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> OrderDetails(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.ProductKeys)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == user.Id);

        if (order == null) return NotFound();

        var model = new OrderDetailsViewModel
        {
            Order = order,
            Keys = order.OrderItems.SelectMany(oi => oi.ProductKeys)
        };

        return View(model);
    }

    public IActionResult AccessDenied()
    {
        return View();
    }
}
