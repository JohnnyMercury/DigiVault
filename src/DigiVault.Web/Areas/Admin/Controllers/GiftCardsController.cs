using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class GiftCardsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService;

    public GiftCardsController(ApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<IActionResult> Index()
    {
        var giftCards = await _context.GiftCards
            .Include(g => g.Products)
            .OrderBy(g => g.Category)
            .ThenBy(g => g.SortOrder)
            .ToListAsync();

        return View(giftCards);
    }

    public IActionResult Create()
    {
        return View(new GiftCard { SortOrder = 100 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GiftCard card, IFormFile? imageFile)
    {
        ModelState.Remove("ImageUrl");

        if (ModelState.IsValid)
        {
            if (imageFile != null)
            {
                card.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            card.Slug = card.Slug.ToLower();
            card.CreatedAt = DateTime.UtcNow;
            _context.GiftCards.Add(card);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Карта успешно создана";
            return RedirectToAction(nameof(Index));
        }

        return View(card);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var card = await _context.GiftCards.FindAsync(id);
        if (card == null)
            return NotFound();

        return View(card);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, GiftCard card, IFormFile? imageFile)
    {
        if (id != card.Id)
            return NotFound();

        ModelState.Remove("ImageUrl");

        if (ModelState.IsValid)
        {
            var existing = await _context.GiftCards.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.Name = card.Name;
            existing.Slug = card.Slug.ToLower();
            existing.Description = card.Description;
            existing.Icon = card.Icon;
            existing.Gradient = card.Gradient;
            existing.Category = card.Category;
            existing.SortOrder = card.SortOrder;
            existing.IsActive = card.IsActive;

            if (imageFile != null)
            {
                if (!string.IsNullOrEmpty(existing.ImageUrl) && existing.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(existing.ImageUrl);
                }
                existing.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }
            else if (!string.IsNullOrEmpty(card.ImageUrl))
            {
                existing.ImageUrl = card.ImageUrl;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Карта успешно обновлена";
            return RedirectToAction(nameof(Index));
        }

        return View(card);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var card = await _context.GiftCards.FindAsync(id);
        if (card != null)
        {
            card.IsActive = false;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Карта скрыта";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var card = await _context.GiftCards.FindAsync(id);
        if (card != null)
        {
            card.IsActive = true;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Карта восстановлена";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Destroy(int id)
    {
        var card = await _context.GiftCards
            .Include(g => g.Products)
                .ThenInclude(p => p.ProductKeys)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (card != null)
        {
            if (!string.IsNullOrEmpty(card.ImageUrl) && card.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(card.ImageUrl);
            }
            foreach (var product in card.Products)
            {
                if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(product.ImageUrl);
                }
            }
            _context.GiftCards.Remove(card);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Карта полностью удалена";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyImageToAllProducts(int id, IFormFile? imageFile, string? imageUrl)
    {
        var card = await _context.GiftCards
            .Include(g => g.Products)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (card == null)
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
        foreach (var product in card.Products)
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
        var card = await _context.GiftCards
            .Include(g => g.Products.OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(g => g.Id == id);

        if (card == null)
            return NotFound();

        return View(card);
    }

    public async Task<IActionResult> CreateProduct(int id)
    {
        var card = await _context.GiftCards.FindAsync(id);
        if (card == null)
            return NotFound();

        ViewBag.GiftCard = card;
        return View(new GameProduct { GiftCardId = id, SortOrder = 100, IsActive = true, ProductType = GameProductType.GiftCard });
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
            {
                product.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            product.CreatedAt = DateTime.UtcNow;
            _context.GameProducts.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Товар успешно создан";
            return RedirectToAction(nameof(Products), new { id = product.GiftCardId });
        }

        var card = await _context.GiftCards.FindAsync(product.GiftCardId);
        ViewBag.GiftCard = card;
        return View(product);
    }

    public async Task<IActionResult> EditProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.GiftCard)
            .FirstOrDefaultAsync(p => p.Id == id && p.GiftCardId != null);

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
            return RedirectToAction(nameof(Products), new { id = existing.GiftCardId });
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
            .Include(p => p.ProductKeys)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product != null)
        {
            var cardId = product.GiftCardId;
            if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(product.ImageUrl);
            }
            _context.GameProducts.Remove(product);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Товар удалён";
            return RedirectToAction(nameof(Products), new { id = cardId });
        }

        return RedirectToAction(nameof(Index));
    }
}
