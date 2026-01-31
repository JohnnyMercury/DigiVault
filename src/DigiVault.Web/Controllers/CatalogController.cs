using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Controllers;

public class CatalogController : Controller
{
    private readonly ApplicationDbContext _context;
    private const int PageSize = 12;

    public CatalogController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(ProductCategory? category = null, string? search = null, string? sort = null, int page = 1)
    {
        var query = _context.Products
            .Where(p => p.IsActive);

        if (category.HasValue)
            query = query.Where(p => p.Category == category.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.ToLower().Contains(search.ToLower()) ||
                                    p.Description.ToLower().Contains(search.ToLower()));

        query = sort switch
        {
            "price_asc" => query.OrderBy(p => p.Price),
            "price_desc" => query.OrderByDescending(p => p.Price),
            "name" => query.OrderBy(p => p.Name),
            "newest" => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderByDescending(p => p.OldPrice.HasValue).ThenByDescending(p => p.CreatedAt)
        };

        var totalProducts = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalProducts / (double)PageSize);

        var products = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var model = new CatalogViewModel
        {
            Products = products.Select(ProductViewModel.FromEntity).ToList(),
            Category = category,
            SearchQuery = search,
            SortBy = sort,
            CurrentPage = page,
            TotalPages = totalPages,
            TotalProducts = totalProducts
        };

        return View(model);
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);

        if (product == null)
            return NotFound();

        var relatedProducts = await _context.Products
            .Where(p => p.Category == product.Category && p.Id != id && p.IsActive && p.StockQuantity > 0)
            .Take(4)
            .ToListAsync();

        var model = new ProductDetailsViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            OldPrice = product.OldPrice,
            Category = product.Category,
            ImageUrl = product.ImageUrl,
            StockQuantity = product.StockQuantity,
            Metadata = product.Metadata,
            RelatedProducts = relatedProducts.Select(ProductViewModel.FromEntity).ToList()
        };

        return View(model);
    }

    public IActionResult Vpn()
    {
        return RedirectToAction("Index", new { category = ProductCategory.VpnSubscription });
    }

    public IActionResult GameCurrency()
    {
        return RedirectToAction("Index", new { category = ProductCategory.GameCurrency });
    }

    public IActionResult GiftCards()
    {
        return RedirectToAction("Index", new { category = ProductCategory.GiftCard });
    }
}
