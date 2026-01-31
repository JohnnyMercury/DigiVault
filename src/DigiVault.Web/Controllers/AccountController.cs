using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
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
    private readonly IOrderService _orderService;
    private readonly IPaymentService _paymentService;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext context,
        IOrderService orderService,
        IPaymentService paymentService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
        _orderService = orderService;
        _paymentService = paymentService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
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

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Account locked out. Please try again later.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,
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

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

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
    public async Task<IActionResult> Profile()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var orderStats = await _context.Orders
            .Where(o => o.UserId == user.Id)
            .GroupBy(o => 1)
            .Select(g => new
            {
                TotalOrders = g.Count(),
                TotalSpent = g.Sum(o => o.TotalAmount)
            })
            .FirstOrDefaultAsync();

        var model = new ProfileViewModel
        {
            Email = user.Email ?? "",
            Balance = user.Balance,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            TotalOrders = orderStats?.TotalOrders ?? 0,
            TotalSpent = orderStats?.TotalSpent ?? 0
        };

        return View(model);
    }

    [Authorize]
    [HttpGet]
    public IActionResult ChangePassword()
    {
        return View();
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);

        if (result.Succeeded)
        {
            TempData["SuccessMessage"] = "Password changed successfully";
            return RedirectToAction("Profile");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Deposit()
    {
        var user = await _userManager.GetUserAsync(User);
        ViewBag.CurrentBalance = user?.Balance ?? 0;
        return View(new DepositViewModel());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deposit(DepositViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        ViewBag.CurrentBalance = user.Balance;

        if (!ModelState.IsValid)
            return View(model);

        var (success, transactionId, error) = await _paymentService.ProcessDepositAsync(
            user.Id,
            model.Amount,
            model.PaymentMethod);

        if (success)
        {
            TempData["SuccessMessage"] = $"Successfully deposited ${model.Amount:F2}. Transaction ID: {transactionId}";
            return RedirectToAction("Profile");
        }

        ModelState.AddModelError(string.Empty, error ?? "Failed to process deposit");
        return View(model);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Orders(int page = 1)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var model = await _orderService.GetOrderHistoryAsync(user.Id, page);
        return View(model);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> OrderDetails(string orderNumber)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var order = await _orderService.GetOrderByNumberAsync(user.Id, orderNumber);
        if (order == null)
            return NotFound();

        return View(order);
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Transactions()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login");

        var transactions = await _orderService.GetTransactionsAsync(user.Id);
        ViewBag.CurrentBalance = user.Balance;
        return View(transactions);
    }

    public IActionResult AccessDenied()
    {
        return View();
    }
}
