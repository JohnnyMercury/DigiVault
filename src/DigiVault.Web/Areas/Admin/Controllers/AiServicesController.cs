using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

/// <summary>
/// CRUD for top-level AI-service brands (ChatGPT Plus, Claude Pro, etc.)
/// and the tariffs underneath them. Mirrors <see cref="VpnProvidersController"/>
/// because the data model + UX is structurally identical — one parent record
/// holds the marketing card, child <see cref="GameProduct"/> rows hold the
/// concrete plans (1 month / 3 months / 1 year). Delivery is the same
/// ContactSupport flow used for Steam Wallet.
/// </summary>
public class AiServicesController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService;
    private readonly ILogger<AiServicesController> _logger;

    public AiServicesController(ApplicationDbContext context, IFileService fileService, ILogger<AiServicesController> logger)
    {
        _context = context;
        _fileService = fileService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var services = await _context.AiServices
            .Include(s => s.Products)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        return View(services);
    }

    public IActionResult Create()
    {
        return View(new AiService { SortOrder = 100 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AiService service, IFormFile? imageFile)
    {
        ModelState.Remove("ImageUrl");

        if (ModelState.IsValid)
        {
            if (imageFile != null)
            {
                service.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            service.Slug = service.Slug.ToLower();
            service.CreatedAt = DateTime.UtcNow;
            _context.AiServices.Add(service);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "AI-сервис успешно создан";
            return RedirectToAction(nameof(Index));
        }

        return View(service);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var service = await _context.AiServices.FindAsync(id);
        if (service == null)
            return NotFound();

        return View(service);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AiService service, IFormFile? imageFile)
    {
        if (id != service.Id)
            return NotFound();

        ModelState.Remove("ImageUrl");

        if (ModelState.IsValid)
        {
            var existing = await _context.AiServices.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.Name = service.Name;
            existing.Slug = service.Slug.ToLower();
            existing.Description = service.Description;
            existing.Tagline = service.Tagline;
            existing.Features = service.Features;
            existing.Icon = service.Icon;
            existing.Gradient = service.Gradient;
            existing.SortOrder = service.SortOrder;
            existing.IsActive = service.IsActive;

            if (imageFile != null)
            {
                if (!string.IsNullOrEmpty(existing.ImageUrl) && existing.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(existing.ImageUrl);
                }
                existing.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }
            else if (!string.IsNullOrEmpty(service.ImageUrl))
            {
                existing.ImageUrl = service.ImageUrl;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "AI-сервис обновлён";
            return RedirectToAction(nameof(Index));
        }

        return View(service);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var service = await _context.AiServices.FindAsync(id);
        if (service != null)
        {
            service.IsActive = false;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "AI-сервис скрыт";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var service = await _context.AiServices.FindAsync(id);
        if (service != null)
        {
            service.IsActive = true;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "AI-сервис восстановлен";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Destroy(int id)
    {
        var service = await _context.AiServices
            .Include(s => s.Products)
                .ThenInclude(p => p.ProductKeys)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (service != null)
        {
            if (!string.IsNullOrEmpty(service.ImageUrl) && service.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(service.ImageUrl);
            }
            foreach (var product in service.Products)
            {
                if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(product.ImageUrl);
                }
            }
            _context.AiServices.Remove(service);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "AI-сервис полностью удалён";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyImageToAllProducts(int id, IFormFile? imageFile, string? imageUrl)
    {
        var service = await _context.AiServices
            .Include(s => s.Products)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (service == null)
            return NotFound();

        string? newImageUrl = null;

        if (imageFile != null)
        {
            newImageUrl = await _fileService.SaveImageAsync(imageFile);
        }
        else if (!string.IsNullOrEmpty(imageUrl))
        {
            newImageUrl = imageUrl;
        }

        if (string.IsNullOrEmpty(newImageUrl))
        {
            TempData["ErrorMessage"] = "Загрузите картинку или вставьте URL";
            return RedirectToAction(nameof(Products), new { id });
        }

        var count = 0;
        foreach (var product in service.Products)
        {
            product.ImageUrl = newImageUrl;
            product.UpdatedAt = DateTime.UtcNow;
            count++;
        }

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Картинка применена к {count} товарам";
        return RedirectToAction(nameof(Products), new { id });
    }

    // === Products ===

    public async Task<IActionResult> Products(int id)
    {
        var service = await _context.AiServices
            .Include(s => s.Products.OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(s => s.Id == id);

        if (service == null)
            return NotFound();

        return View(service);
    }

    public async Task<IActionResult> CreateProduct(int id)
    {
        var service = await _context.AiServices.FindAsync(id);
        if (service == null)
            return NotFound();

        ViewBag.AiService = service;
        return View(new GameProduct { AiServiceId = id, SortOrder = 100, IsActive = true, ProductType = GameProductType.Subscription });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(GameProduct product, IFormFile? imageFile)
    {
        // Same {id}-route trap as VpnProviders: model binder writes route
        // value into product.Id, breaks INSERT. Zero out first.
        product.Id = 0;
        ModelState.Remove("Id");

        ModelState.Remove("Game");
        ModelState.Remove("GiftCard");
        ModelState.Remove("VpnProvider");
        ModelState.Remove("AiService");
        ModelState.Remove("ProductKeys");

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .Select(x => $"{x.Key}: {string.Join("; ", x.Value!.Errors.Select(e => e.ErrorMessage))}")
                .ToList();
            _logger.LogWarning("CreateProduct (AiService {Id}) validation failed: {Errors}",
                product.AiServiceId, string.Join(" | ", errors));
        }
        else
        {
            try
            {
                if (imageFile != null)
                {
                    product.ImageUrl = await _fileService.SaveImageAsync(imageFile);
                }

                product.CreatedAt = DateTime.UtcNow;
                _context.GameProducts.Add(product);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Товар успешно создан";
                return RedirectToAction(nameof(Products), new { id = product.AiServiceId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateProduct failed for AiService {Id}", product.AiServiceId);
                var detail = ex.InnerException?.Message ?? ex.Message;
                ModelState.AddModelError("", $"Не удалось сохранить товар: {detail}");
            }
        }

        var svc = await _context.AiServices.FindAsync(product.AiServiceId);
        ViewBag.AiService = svc;
        return View(product);
    }

    public async Task<IActionResult> EditProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.AiService)
            .FirstOrDefaultAsync(p => p.Id == id && p.AiServiceId != null);

        if (product == null)
            return NotFound();

        ViewBag.AiService = product.AiService;
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
        ModelState.Remove("AiService");
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
            existing.ProductType = product.ProductType;
            existing.Multiplier = product.Multiplier;
            existing.Region = product.Region;
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
            return RedirectToAction(nameof(Products), new { id = existing.AiServiceId });
        }

        var service = await _context.AiServices.FindAsync(product.AiServiceId);
        ViewBag.AiService = service;
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
            var serviceId = product.AiServiceId;
            if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(product.ImageUrl);
            }
            _context.GameProducts.Remove(product);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Товар удалён";
            return RedirectToAction(nameof(Products), new { id = serviceId });
        }

        return RedirectToAction(nameof(Index));
    }
}
