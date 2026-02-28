using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class ProductsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService;

    public ProductsController(ApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<IActionResult> Index(ProductCategory? category = null, string? search = null)
    {
        var query = _context.Products.AsQueryable();

        if (category.HasValue)
            query = query.Where(p => p.Category == category.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.ToLower().Contains(search.ToLower()));

        var products = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        ViewBag.Category = category;
        ViewBag.Search = search;

        return View(products);
    }

    public IActionResult Create()
    {
        return View(new ProductCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductCreateViewModel model)
    {
        if (ModelState.IsValid)
        {
            var product = new Product
            {
                Name = model.Name,
                Description = model.Description,
                Price = model.Price,
                OldPrice = model.OldPrice,
                Category = model.Category,
                StockQuantity = model.StockQuantity,
                IsActive = model.IsActive,
                IsFeatured = model.IsFeatured,
                Metadata = model.Metadata,
                CreatedAt = DateTime.UtcNow
            };

            // Handle image upload
            if (model.ImageFile != null)
            {
                product.ImageUrl = await _fileService.SaveImageAsync(model.ImageFile);
            }
            else if (!string.IsNullOrEmpty(model.ImageUrl))
            {
                product.ImageUrl = model.ImageUrl;
            }

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Product created successfully";
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return NotFound();

        var model = new ProductEditViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            OldPrice = product.OldPrice,
            Category = product.Category,
            StockQuantity = product.StockQuantity,
            IsActive = product.IsActive,
            IsFeatured = product.IsFeatured,
            Metadata = product.Metadata,
            CurrentImageUrl = product.ImageUrl
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductEditViewModel model)
    {
        if (id != model.Id)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                var product = await _context.Products.FindAsync(id);
                if (product == null)
                    return NotFound();

                product.Name = model.Name;
                product.Description = model.Description;
                product.Price = model.Price;
                product.OldPrice = model.OldPrice;
                product.Category = model.Category;
                product.StockQuantity = model.StockQuantity;
                product.IsActive = model.IsActive;
                product.IsFeatured = model.IsFeatured;
                product.Metadata = model.Metadata;
                product.UpdatedAt = DateTime.UtcNow;

                // Handle image upload
                if (model.ImageFile != null)
                {
                    // Delete old image if exists
                    if (!string.IsNullOrEmpty(product.ImageUrl))
                    {
                        _fileService.DeleteImage(product.ImageUrl);
                    }

                    product.ImageUrl = await _fileService.SaveImageAsync(model.ImageFile);
                }
                else if (!string.IsNullOrEmpty(model.ImageUrl) && model.ImageUrl != model.CurrentImageUrl)
                {
                    // URL was changed manually
                    if (!string.IsNullOrEmpty(product.ImageUrl))
                    {
                        _fileService.DeleteImage(product.ImageUrl);
                    }
                    product.ImageUrl = model.ImageUrl;
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Product updated successfully";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Products.AnyAsync(p => p.Id == id))
                    return NotFound();
                throw;
            }
        }

        model.CurrentImageUrl = model.CurrentImageUrl;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return NotFound();

        // Delete image
        if (!string.IsNullOrEmpty(product.ImageUrl))
        {
            _fileService.DeleteImage(product.ImageUrl);
        }

        product.IsActive = false;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Product deleted successfully";
        return RedirectToAction(nameof(Index));
    }

    // Keys are now auto-generated during purchase (GUID-based)
    // Legacy Keys/AddKeys actions removed
}
