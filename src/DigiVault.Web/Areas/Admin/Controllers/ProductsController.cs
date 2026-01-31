using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class ProductsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public ProductsController(ApplicationDbContext context)
    {
        _context = context;
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
        return View(new Product());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product)
    {
        if (ModelState.IsValid)
        {
            product.CreatedAt = DateTime.UtcNow;
            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Product created successfully";
            return RedirectToAction(nameof(Index));
        }

        return View(product);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return NotFound();

        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product product)
    {
        if (id != product.Id)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                product.UpdatedAt = DateTime.UtcNow;
                _context.Update(product);
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

        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return NotFound();

        product.IsActive = false;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Product deleted successfully";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Keys(int id)
    {
        var product = await _context.Products
            .Include(p => p.ProductKeys)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound();

        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddKeys(int productId, string keys)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
            return NotFound();

        var keyList = keys.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        foreach (var key in keyList)
        {
            _context.ProductKeys.Add(new ProductKey
            {
                ProductId = productId,
                KeyValue = key
            });
        }

        product.StockQuantity += keyList.Count;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Added {keyList.Count} keys";
        return RedirectToAction(nameof(Keys), new { id = productId });
    }
}
