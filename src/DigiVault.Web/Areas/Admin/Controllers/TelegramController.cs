using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class TelegramController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService;

    public TelegramController(ApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<IActionResult> Index()
    {
        // Star settings
        var starRate = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == "telegram:star_rate");
        var minStars = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == "telegram:min_stars");
        var maxStars = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == "telegram:max_stars");

        ViewBag.StarRate = starRate?.Value ?? "1.5";
        ViewBag.MinStars = minStars?.Value ?? "50";
        ViewBag.MaxStars = maxStars?.Value ?? "25000";

        // Stars card
        var starsCard = await _context.GiftCards
            .Include(g => g.Products)
            .FirstOrDefaultAsync(g => g.Slug == "telegram-stars");
        ViewBag.StarsCard = starsCard;

        // Premium card with products
        var premiumCard = await _context.GiftCards
            .Include(g => g.Products.OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(g => g.Slug == "telegram-premium");
        ViewBag.PremiumCard = premiumCard;

        // Orders stats
        var telegramOrders = await _context.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.OrderItems.Any(oi => oi.GameProduct != null &&
                (oi.GameProduct.GiftCard != null && oi.GameProduct.GiftCard.Category == GiftCardCategory.Telegram)))
            .CountAsync();
        ViewBag.TelegramOrders = telegramOrders;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStarSettings(string starRate, string minStars, string maxStars)
    {
        // Validate
        if (!decimal.TryParse(starRate.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rate) || rate <= 0)
        {
            TempData["ErrorMessage"] = "Неверный курс звёзд";
            return RedirectToAction(nameof(Index));
        }

        if (!int.TryParse(minStars, out var min) || min < 1)
        {
            TempData["ErrorMessage"] = "Неверное минимальное количество";
            return RedirectToAction(nameof(Index));
        }

        if (!int.TryParse(maxStars, out var max) || max <= min)
        {
            TempData["ErrorMessage"] = "Максимум должен быть больше минимума";
            return RedirectToAction(nameof(Index));
        }

        await UpsertSettingAsync("telegram:star_rate", rate.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await UpsertSettingAsync("telegram:min_stars", min.ToString());
        await UpsertSettingAsync("telegram:max_stars", max.ToString());

        TempData["SuccessMessage"] = "Настройки звёзд обновлены";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStarsImage(IFormFile imageFile)
    {
        var starsCard = await _context.GiftCards.FirstOrDefaultAsync(g => g.Slug == "telegram-stars");
        if (starsCard == null)
        {
            TempData["ErrorMessage"] = "Карта Звёзды Telegram не найдена";
            return RedirectToAction(nameof(Index));
        }

        if (imageFile == null || imageFile.Length == 0)
        {
            TempData["ErrorMessage"] = "Выберите изображение";
            return RedirectToAction(nameof(Index));
        }

        // Delete old image if it's an upload
        if (!string.IsNullOrEmpty(starsCard.ImageUrl) && starsCard.ImageUrl.StartsWith("/images/uploads/"))
            _fileService.DeleteImage(starsCard.ImageUrl);

        starsCard.ImageUrl = await _fileService.SaveImageAsync(imageFile);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Изображение обновлено";
        return RedirectToAction(nameof(Index));
    }

    // === Premium Products Management ===

    public async Task<IActionResult> CreateProduct()
    {
        var premiumCard = await _context.GiftCards.FirstOrDefaultAsync(g => g.Slug == "telegram-premium");
        if (premiumCard == null)
        {
            TempData["ErrorMessage"] = "Карта Telegram Premium не найдена";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.GiftCard = premiumCard;
        return View(new GameProduct
        {
            GiftCardId = premiumCard.Id,
            SortOrder = 100,
            IsActive = true,
            ProductType = GameProductType.GiftCard,
            StockQuantity = 999
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(GameProduct product, IFormFile? imageFile)
    {
        ModelState.Remove("Game");
        ModelState.Remove("GiftCard");
        ModelState.Remove("ProductKeys");

        if (ModelState.IsValid)
        {
            if (imageFile != null)
                product.ImageUrl = await _fileService.SaveImageAsync(imageFile);

            product.CreatedAt = DateTime.UtcNow;
            _context.GameProducts.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Тариф Premium создан";
            return RedirectToAction(nameof(Index));
        }

        var card = await _context.GiftCards.FindAsync(product.GiftCardId);
        ViewBag.GiftCard = card;
        return View(product);
    }

    public async Task<IActionResult> EditProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.GiftCard)
            .FirstOrDefaultAsync(p => p.Id == id && p.GiftCard != null && p.GiftCard.Category == GiftCardCategory.Telegram);

        if (product == null)
            return NotFound();

        ViewBag.GiftCard = product.GiftCard;
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProduct(int id, GameProduct product, IFormFile? imageFile)
    {
        if (id != product.Id)
            return NotFound();

        ModelState.Remove("Game");
        ModelState.Remove("GiftCard");
        ModelState.Remove("ProductKeys");

        if (ModelState.IsValid)
        {
            var existing = await _context.GameProducts.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.Name = product.Name;
            existing.Amount = product.Amount;
            existing.Price = product.Price;
            existing.OldPrice = product.OldPrice;
            existing.Discount = product.Discount;
            existing.Region = product.Region;
            existing.SortOrder = product.SortOrder;
            existing.IsActive = product.IsActive;
            existing.IsFeatured = product.IsFeatured;
            existing.StockQuantity = product.StockQuantity;
            existing.UpdatedAt = DateTime.UtcNow;

            if (imageFile != null)
            {
                if (!string.IsNullOrEmpty(existing.ImageUrl))
                    _fileService.DeleteImage(existing.ImageUrl);
                existing.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }
            else if (!string.IsNullOrEmpty(product.ImageUrl))
            {
                existing.ImageUrl = product.ImageUrl;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Тариф обновлён";
            return RedirectToAction(nameof(Index));
        }

        var card = await _context.GiftCards.FindAsync(product.GiftCardId);
        ViewBag.GiftCard = card;
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.GiftCard)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product != null)
        {
            if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
                _fileService.DeleteImage(product.ImageUrl);

            _context.GameProducts.Remove(product);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Тариф удалён";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleProduct(int id)
    {
        var product = await _context.GameProducts.FindAsync(id);
        if (product != null)
        {
            product.IsActive = !product.IsActive;
            product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = product.IsActive ? "Тариф активирован" : "Тариф скрыт";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task UpsertSettingAsync(string key, string value)
    {
        var setting = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null)
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        }
        await _context.SaveChangesAsync();
    }
}
