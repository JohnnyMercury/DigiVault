using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin panel for the Steam Wallet top-up tile (the slider page at
/// /Catalog/Steam). Mirrors the structure of <see cref="TelegramController"/>:
///   - Index page lists current card metadata + slider settings + stats.
///   - Image upload, card-text edit and slider-config edit are separate POST
///     endpoints so each form on the page is small and independent.
///
/// Slider settings (min / max / step / default / bonus %) are stored as
/// AppSettings rows under the `steam:*` key prefix. The /Catalog/Steam view
/// reads them from ViewBag set by CatalogController.
/// </summary>
public class SteamController : AdminBaseController
{
    private const string Slug = "steam-wallet";

    // AppSettings keys — single source of truth.
    public const string KeyMin     = "steam:min_amount";
    public const string KeyMax     = "steam:max_amount";
    public const string KeyStep    = "steam:step";
    public const string KeyDefault = "steam:default_amount";
    public const string KeyBonus   = "steam:bonus_percent";

    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService;

    public SteamController(ApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<IActionResult> Index()
    {
        var card = await _context.GiftCards.FirstOrDefaultAsync(g => g.Slug == Slug);
        ViewBag.Card = card;

        var settings = await _context.AppSettings
            .Where(s => s.Key.StartsWith("steam:"))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        ViewBag.MinAmount     = settings.GetValueOrDefault(KeyMin, "100");
        ViewBag.MaxAmount     = settings.GetValueOrDefault(KeyMax, "15000");
        ViewBag.StepAmount    = settings.GetValueOrDefault(KeyStep, "50");
        ViewBag.DefaultAmount = settings.GetValueOrDefault(KeyDefault, "1000");
        ViewBag.BonusPercent  = settings.GetValueOrDefault(KeyBonus, "10");

        // Stats — orders that include the steam-wallet anchor product.
        var orders = card == null ? 0 : await _context.Orders
            .CountAsync(o => o.OrderItems.Any(oi =>
                oi.GameProduct != null && oi.GameProduct.GiftCardId == card.Id));
        ViewBag.OrdersCount = orders;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateImage(IFormFile imageFile)
    {
        var card = await _context.GiftCards.FirstOrDefaultAsync(g => g.Slug == Slug);
        if (card == null)
        {
            TempData["ErrorMessage"] = "Карточка Steam Wallet не найдена";
            return RedirectToAction(nameof(Index));
        }
        if (imageFile == null || imageFile.Length == 0)
        {
            TempData["ErrorMessage"] = "Выберите изображение";
            return RedirectToAction(nameof(Index));
        }

        if (!string.IsNullOrEmpty(card.ImageUrl) && card.ImageUrl.StartsWith("/images/uploads/"))
            _fileService.DeleteImage(card.ImageUrl);

        card.ImageUrl = await _fileService.SaveImageAsync(imageFile);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Изображение обновлено";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCard(string name, string? description, string? gradient, string? icon)
    {
        var card = await _context.GiftCards.FirstOrDefaultAsync(g => g.Slug == Slug);
        if (card == null)
        {
            TempData["ErrorMessage"] = "Карточка Steam Wallet не найдена";
            return RedirectToAction(nameof(Index));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "Название не может быть пустым";
            return RedirectToAction(nameof(Index));
        }

        card.Name = name.Trim();
        card.Description = description?.Trim();
        if (!string.IsNullOrWhiteSpace(gradient)) card.Gradient = gradient.Trim();
        if (!string.IsNullOrWhiteSpace(icon))     card.Icon = icon.Trim();
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Карточка обновлена";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateSettings(
        decimal minAmount, decimal maxAmount, decimal stepAmount,
        decimal defaultAmount, decimal bonusPercent)
    {
        if (minAmount < 1 || maxAmount <= minAmount)
        {
            TempData["ErrorMessage"] = "Минимум должен быть положительным, максимум - больше минимума";
            return RedirectToAction(nameof(Index));
        }
        if (stepAmount < 1) stepAmount = 1;
        if (defaultAmount < minAmount) defaultAmount = minAmount;
        if (defaultAmount > maxAmount) defaultAmount = maxAmount;
        if (bonusPercent < 0)   bonusPercent = 0;
        if (bonusPercent > 100) bonusPercent = 100;

        await SetAsync(KeyMin,     minAmount.ToString("0"),     "Steam slider: minimum amount in RUB");
        await SetAsync(KeyMax,     maxAmount.ToString("0"),     "Steam slider: maximum amount in RUB");
        await SetAsync(KeyStep,    stepAmount.ToString("0"),    "Steam slider: step in RUB");
        await SetAsync(KeyDefault, defaultAmount.ToString("0"), "Steam slider: default value");
        await SetAsync(KeyBonus,   bonusPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            "Steam: bonus percentage credited to Steam wallet on top of paid amount");

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Настройки слайдера сохранены";
        return RedirectToAction(nameof(Index));
    }

    private async Task SetAsync(string key, string value, string description)
    {
        var existing = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing != null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                Description = description,
                UpdatedAt = DateTime.UtcNow,
            });
        }
    }
}
