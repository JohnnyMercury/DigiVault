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
    private readonly IOrderService _orderService;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext context,
        IPaymentService paymentService,
        IOrderService orderService,
        IConfiguration config,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
        _paymentService = paymentService;
        _orderService = orderService;
        _config = config;
        _logger = logger;
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

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ViewBag.Error = "Неверный email или пароль";
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
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
            .ThenInclude(oi => oi.GameProduct)
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
                    ProductId = oi.GameProductId,
                    ProductName = oi.GameProduct?.Name ?? "Unknown",
                    ImageUrl = oi.GameProduct?.ImageUrl,
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

        if (model.Amount > 100000)
        {
            ModelState.AddModelError("Amount", "Максимальная сумма — 100 000 ₽");
            return View(model);
        }

        var rawMethod = (model.PaymentMethod ?? "card").ToLowerInvariant();

        // Админ-начисление: только когда админ явно выбрал этот метод —
        // обычные пополнения идут через PSP даже у админа.
        if (rawMethod == "admin")
        {
            if (!User.IsInRole("Admin"))
            {
                TempData["Error"] = "Этот способ доступен только администраторам";
                return RedirectToAction("Deposit");
            }

            user.Balance += model.Amount;
            await _userManager.UpdateAsync(user);

            _context.Transactions.Add(new Transaction
            {
                UserId = user.Id,
                Amount = model.Amount,
                Type = TransactionType.Deposit,
                Description = "Админ-начисление",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Баланс пополнен на {model.Amount:N0} ₽. Новый баланс: {user.Balance:N0} ₽";
            return RedirectToAction("Dashboard");
        }

        // Реальное пополнение через PSP. Реестр методов и факт «доступен ли
        // PSP под этот код» — в DigiVault.Web.Services.Payment.PaymentMethodCatalog.
        // Здесь больше нет своего switch — добавляем новые методы в каталог,
        // и пополнение баланса автоматически их подхватывает.
        if (!Services.Payment.PaymentMethodCatalog.IsAvailable(rawMethod))
        {
            TempData["Error"] = "Этот метод пополнения скоро станет доступен. Используйте «Банковскую карту» или «СБП».";
            return RedirectToAction("Deposit");
        }

        var pspMethod = Services.Payment.PaymentMethodCatalog.ToEnum(rawMethod);
        var clientIp  = HttpContext.Connection.RemoteIpAddress?.ToString();
        var siteBase  = $"{Request.Scheme}://{Request.Host}";
        var result    = await _paymentService.CreateDepositAsync(
            user.Id, model.Amount, pspMethod, clientIp, siteBase, model.Provider);

        if (!result.Success || string.IsNullOrEmpty(result.RedirectUrl))
        {
            TempData["Error"] = result.ErrorMessage ?? "Не удалось создать платёж. Попробуйте ещё раз.";
            return RedirectToAction("Deposit");
        }

        return Redirect(result.RedirectUrl);
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
    public async Task<IActionResult> OrderDetails(string orderNumber)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return RedirectToAction("Login");

        var model = await _orderService.GetOrderByNumberAsync(user.Id, orderNumber);
        if (model == null) return NotFound();

        // Used by _DeliveryPayload partial in the «delivered but no payload»
        // fallback branch (legacy orders, deserialisation failures).
        ViewBag.SupportTelegramUsername = (_config["Support:TelegramUsername"] ?? "key_zona_support")
            .TrimStart('@').Trim();

        return View(model);
    }

    public IActionResult AccessDenied()
    {
        return View();
    }

    /// <summary>
    /// Landing after a successful external payment. The provider's webhook
    /// has likely already flipped the order to Completed (or will within
    /// seconds via the fulfilment background sweeper). We just route the
    /// user to the order details page.
    /// </summary>
    [HttpGet, HttpPost]
    [Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryToken]
    public async Task<IActionResult> PaymentSuccess(int orderId)
    {
        // PaymentLink (and some other PSPs) POST the user back to backURL
        // with form-data containing the payment result. We deliberately don't
        // trust that body — webhook is the source of truth — and just route
        // the user to a friendly destination. [Authorize] is intentionally
        // omitted: a top-level cross-site POST often arrives without our
        // SameSite=Lax auth cookie, so requiring auth here would bounce the
        // user to /Login and lose the orderId context.
        await LogPaymentReturnPayloadAsync(orderId, "PaymentSuccess");

        // PaymentLink semantics: backURL is hit on BOTH success and failure;
        // an `errorcode` field in the POST body marks failure even though
        // we're on the "success" route. Treat it as failure so the user sees
        // the real reason instead of a misleading "оплата прошла" message.
        if (await IsFailureReturnAsync())
        {
            return await PaymentFail(orderId);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            // Public-facing "we got your payment" landing — they can log back
            // in and find the order in their cabinet.
            TempData["SuccessMessage"] = "Оплата прошла успешно. Войдите в аккаунт чтобы увидеть товар.";
            return RedirectToAction(nameof(Login));
        }

        var order = await _orderService.GetOrderAsync(user.Id, orderId);
        if (order == null) return RedirectToAction(nameof(Orders));

        TempData["SuccessMessage"] = "Оплата прошла успешно. Товар будет в кабинете в течение минуты.";
        return RedirectToAction(nameof(OrderDetails), new { orderNumber = order.OrderNumber });
    }

    /// <summary>
    /// Landing after a cancelled/failed external payment.
    /// Accepts POST too — same reasoning as <see cref="PaymentSuccess"/>.
    /// </summary>
    [HttpGet, HttpPost]
    [Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryToken]
    public async Task<IActionResult> PaymentFail(int orderId)
    {
        await LogPaymentReturnPayloadAsync(orderId, "PaymentFail");

        var errorText = ExtractErrorText();

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            TempData["ErrorMessage"] = errorText ?? "Платёж не был завершён. Войдите чтобы попробовать снова.";
            return RedirectToAction(nameof(Login));
        }

        var order = await _orderService.GetOrderAsync(user.Id, orderId);
        TempData["ErrorMessage"] = errorText ?? "Платёж не был завершён. Попробуйте ещё раз или свяжитесь с поддержкой.";
        if (order != null)
            return RedirectToAction(nameof(OrderDetails), new { orderNumber = order.OrderNumber });
        return RedirectToAction(nameof(Orders));
    }

    // ────────────────────────────────────────────────────────────────────
    // Payment-return helpers (PaymentLink/Enot/Overpay all POST or GET back
    // here with provider-specific status fields). We never trust this body
    // for fulfilment - that's the webhook's job - we just want to:
    //   • leave a breadcrumb in logs so we can diagnose silent-redirect
    //     failures (PaymentLink test server rejecting form parameters etc.);
    //   • surface a meaningful error message to the user when the provider
    //     told us why the charge failed.
    // ────────────────────────────────────────────────────────────────────

    private async Task LogPaymentReturnPayloadAsync(int orderId, string source)
    {
        try
        {
            var fields = new List<string>();
            foreach (var kv in Request.Query)
                fields.Add($"q.{kv.Key}={kv.Value}");

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var kv in form)
                {
                    // Mask PAN-like fields just in case some provider posts them
                    var v = kv.Value.ToString();
                    if (kv.Key.Equals("PAN", StringComparison.OrdinalIgnoreCase)
                     || kv.Key.Equals("pan", StringComparison.OrdinalIgnoreCase))
                        v = "<masked>";
                    fields.Add($"f.{kv.Key}={v}");
                }
            }

            _logger.LogInformation(
                "{Source} payload for orderId={OrderId}: method={Method}, contentType={CT}, {Fields}",
                source, orderId, Request.Method, Request.ContentType,
                string.Join(", ", fields));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log payment return payload for {Source}", source);
        }
    }

    private async Task<bool> IsFailureReturnAsync()
    {
        if (Request.Query.TryGetValue("errorcode", out var qec) && !string.IsNullOrEmpty(qec))
            return true;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            if (form.TryGetValue("errorcode", out var fec) && !string.IsNullOrEmpty(fec))
                return true;
        }
        return false;
    }

    private string? ExtractErrorText()
    {
        if (Request.Query.TryGetValue("errortext", out var qet) && !string.IsNullOrEmpty(qet))
            return $"PSP: {Uri.UnescapeDataString(qet!)}";
        if (Request.HasFormContentType && Request.Form.TryGetValue("errortext", out var fet) && !string.IsNullOrEmpty(fet))
            return $"PSP: {Uri.UnescapeDataString(fet!)}";
        return null;
    }
}
