using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class VpnProvidersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService;

    public VpnProvidersController(ApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<IActionResult> Index()
    {
        var providers = await _context.VpnProviders
            .Include(v => v.Products)
            .OrderBy(v => v.SortOrder)
            .ToListAsync();

        return View(providers);
    }

    public IActionResult Create()
    {
        return View(new VpnProvider { SortOrder = 100 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(VpnProvider provider, IFormFile? imageFile)
    {
        ModelState.Remove("ImageUrl");

        if (ModelState.IsValid)
        {
            if (imageFile != null)
            {
                provider.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            provider.Slug = provider.Slug.ToLower();
            provider.CreatedAt = DateTime.UtcNow;
            _context.VpnProviders.Add(provider);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "VPN провайдер успешно создан";
            return RedirectToAction(nameof(Index));
        }

        return View(provider);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var provider = await _context.VpnProviders.FindAsync(id);
        if (provider == null)
            return NotFound();

        return View(provider);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, VpnProvider provider, IFormFile? imageFile)
    {
        if (id != provider.Id)
            return NotFound();

        ModelState.Remove("ImageUrl");

        if (ModelState.IsValid)
        {
            var existing = await _context.VpnProviders.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.Name = provider.Name;
            existing.Slug = provider.Slug.ToLower();
            existing.Description = provider.Description;
            existing.Tagline = provider.Tagline;
            existing.Features = provider.Features;
            existing.Icon = provider.Icon;
            existing.Gradient = provider.Gradient;
            existing.SortOrder = provider.SortOrder;
            existing.IsActive = provider.IsActive;

            if (imageFile != null)
            {
                if (!string.IsNullOrEmpty(existing.ImageUrl) && existing.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(existing.ImageUrl);
                }
                existing.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }
            else if (!string.IsNullOrEmpty(provider.ImageUrl))
            {
                existing.ImageUrl = provider.ImageUrl;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "VPN провайдер обновлён";
            return RedirectToAction(nameof(Index));
        }

        return View(provider);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var provider = await _context.VpnProviders.FindAsync(id);
        if (provider != null)
        {
            provider.IsActive = false;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "VPN провайдер скрыт";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var provider = await _context.VpnProviders.FindAsync(id);
        if (provider != null)
        {
            provider.IsActive = true;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "VPN провайдер восстановлен";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Destroy(int id)
    {
        var provider = await _context.VpnProviders
            .Include(v => v.Products)
                .ThenInclude(p => p.ProductKeys)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (provider != null)
        {
            if (!string.IsNullOrEmpty(provider.ImageUrl) && provider.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(provider.ImageUrl);
            }
            foreach (var product in provider.Products)
            {
                if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(product.ImageUrl);
                }
            }
            _context.VpnProviders.Remove(provider);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "VPN провайдер полностью удалён";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyImageToAllProducts(int id)
    {
        var provider = await _context.VpnProviders
            .Include(v => v.Products)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (provider == null)
            return NotFound();

        if (string.IsNullOrEmpty(provider.ImageUrl))
        {
            TempData["ErrorMessage"] = "У провайдера нет картинки. Сначала загрузите картинку провайдеру.";
            return RedirectToAction(nameof(Products), new { id });
        }

        var count = 0;
        foreach (var product in provider.Products)
        {
            product.ImageUrl = provider.ImageUrl;
            product.UpdatedAt = DateTime.UtcNow;
            count++;
        }

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Картинка провайдера применена к {count} товарам";
        return RedirectToAction(nameof(Products), new { id });
    }

    // === Products ===

    public async Task<IActionResult> Products(int id)
    {
        var provider = await _context.VpnProviders
            .Include(v => v.Products.OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(v => v.Id == id);

        if (provider == null)
            return NotFound();

        return View(provider);
    }

    public async Task<IActionResult> CreateProduct(int id)
    {
        var provider = await _context.VpnProviders.FindAsync(id);
        if (provider == null)
            return NotFound();

        ViewBag.VpnProvider = provider;
        return View(new GameProduct { VpnProviderId = id, SortOrder = 100, IsActive = true, ProductType = GameProductType.Subscription });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(GameProduct product, IFormFile? imageFile)
    {
        ModelState.Remove("Game");
        ModelState.Remove("GiftCard");
        ModelState.Remove("VpnProvider");
        ModelState.Remove("ProductKeys");

        if (ModelState.IsValid)
        {
            if (imageFile != null)
            {
                product.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            product.CreatedAt = DateTime.UtcNow;
            _context.GameProducts.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Товар успешно создан";
            return RedirectToAction(nameof(Products), new { id = product.VpnProviderId });
        }

        var provider = await _context.VpnProviders.FindAsync(product.VpnProviderId);
        ViewBag.VpnProvider = provider;
        return View(product);
    }

    public async Task<IActionResult> EditProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.VpnProvider)
            .FirstOrDefaultAsync(p => p.Id == id && p.VpnProviderId != null);

        if (product == null)
            return NotFound();

        ViewBag.VpnProvider = product.VpnProvider;
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
        ModelState.Remove("VpnProvider");
        ModelState.Remove("ProductKeys");

        if (ModelState.IsValid)
        {
            var existing = await _context.GameProducts.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.Name = product.Name;
            existing.Amount = product.Amount;
            existing.Bonus = product.Bonus;
            existing.TotalDisplay = product.TotalDisplay;
            existing.Price = product.Price;
            existing.OldPrice = product.OldPrice;
            existing.Discount = product.Discount;
            existing.SortOrder = product.SortOrder;
            existing.IsActive = product.IsActive;
            existing.IsFeatured = product.IsFeatured;
            existing.StockQuantity = product.StockQuantity;
            existing.UpdatedAt = DateTime.UtcNow;

            if (imageFile != null)
            {
                if (!string.IsNullOrEmpty(existing.ImageUrl) && existing.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(existing.ImageUrl);
                }
                existing.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }
            else if (!string.IsNullOrEmpty(product.ImageUrl))
            {
                existing.ImageUrl = product.ImageUrl;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Товар обновлён";
            return RedirectToAction(nameof(Products), new { id = existing.VpnProviderId });
        }

        var provider = await _context.VpnProviders.FindAsync(product.VpnProviderId);
        ViewBag.VpnProvider = provider;
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.ProductKeys)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product != null)
        {
            var providerId = product.VpnProviderId;
            if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(product.ImageUrl);
            }
            _context.GameProducts.Remove(product);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Товар удалён";
            return RedirectToAction(nameof(Products), new { id = providerId });
        }

        return RedirectToAction(nameof(Index));
    }
}
