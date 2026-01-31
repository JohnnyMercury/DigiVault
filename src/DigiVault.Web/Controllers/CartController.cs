using DigiVault.Core.Entities;
using DigiVault.Web.Services;
using DigiVault.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace DigiVault.Web.Controllers;

[Authorize]
public class CartController : Controller
{
    private readonly ICartService _cartService;
    private readonly IOrderService _orderService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CartController(
        ICartService cartService,
        IOrderService orderService,
        UserManager<ApplicationUser> userManager)
    {
        _cartService = cartService;
        _orderService = orderService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null)
            return RedirectToAction("Login", "Account");

        var cart = await _cartService.GetCartAsync(userId);
        return View(cart);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddToCartViewModel model)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null)
            return Json(new { success = false, message = "Please login first" });

        var result = await _cartService.AddToCartAsync(userId, model.ProductId, model.Quantity);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            if (result)
            {
                var count = await _cartService.GetCartItemCountAsync(userId);
                return Json(new { success = true, cartCount = count });
            }
            return Json(new { success = false, message = "Failed to add item to cart" });
        }

        if (result)
            TempData["SuccessMessage"] = "Item added to cart";
        else
            TempData["ErrorMessage"] = "Failed to add item to cart";

        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(UpdateCartItemViewModel model)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null)
            return RedirectToAction("Login", "Account");

        await _cartService.UpdateQuantityAsync(userId, model.ProductId, model.Quantity);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            var cart = await _cartService.GetCartAsync(userId);
            return Json(new
            {
                success = true,
                subtotal = cart.Subtotal,
                total = cart.Total,
                cartCount = cart.TotalItems
            });
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId)
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null)
            return RedirectToAction("Login", "Account");

        await _cartService.RemoveFromCartAsync(userId, productId);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            var cart = await _cartService.GetCartAsync(userId);
            return Json(new
            {
                success = true,
                subtotal = cart.Subtotal,
                total = cart.Total,
                cartCount = cart.TotalItems,
                isEmpty = cart.IsEmpty
            });
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear()
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null)
            return RedirectToAction("Login", "Account");

        await _cartService.ClearCartAsync(userId);
        return RedirectToAction("Index");
    }

    public async Task<IActionResult> Checkout()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login", "Account");

        var cart = await _cartService.GetCartAsync(user.Id);
        if (cart.IsEmpty)
            return RedirectToAction("Index");

        var model = new CheckoutViewModel
        {
            Cart = cart,
            UserBalance = user.Balance
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("Login", "Account");

        var (success, orderNumber, error) = await _orderService.CreateOrderAsync(user.Id);

        if (success)
        {
            TempData["SuccessMessage"] = "Order placed successfully!";
            return RedirectToAction("OrderDetails", "Account", new { orderNumber });
        }

        TempData["ErrorMessage"] = error ?? "Failed to place order";
        return RedirectToAction("Checkout");
    }

    [HttpGet]
    public async Task<IActionResult> GetCount()
    {
        var userId = _userManager.GetUserId(User);
        if (userId == null)
            return Json(new { count = 0 });

        var count = await _cartService.GetCartItemCountAsync(userId);
        return Json(new { count });
    }
}
