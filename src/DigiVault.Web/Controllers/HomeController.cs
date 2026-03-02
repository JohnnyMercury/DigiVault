using DigiVault.Infrastructure.Data;
using DigiVault.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DigiVault.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var items = new List<CatalogItemViewModel>();

        // Games
        var games = await _context.Games
            .Include(g => g.Products.Where(p => p.IsActive && p.StockQuantity > 0))
            .Where(g => g.IsActive)
            .ToListAsync();

        items.AddRange(games.Select(g => new CatalogItemViewModel
        {
            Id = g.Id,
            Name = g.Name,
            Slug = g.Slug,
            ImageUrl = g.ImageUrl,
            Icon = g.Icon,
            Gradient = g.Gradient,
            Category = "games",
            CategoryDisplay = "Игровая валюта",
            DetailUrl = $"/Catalog/Game/{g.Slug}",
            ProductCount = g.Products.Count,
            MinPrice = g.Products.Any() ? g.Products.Min(p => p.Price) : null
        }));

        // Gift Cards
        var giftCards = await _context.GiftCards
            .Include(g => g.Products.Where(p => p.IsActive && p.StockQuantity > 0))
            .Where(g => g.IsActive)
            .ToListAsync();

        items.AddRange(giftCards.Select(g => new CatalogItemViewModel
        {
            Id = g.Id,
            Name = g.Name,
            Slug = g.Slug,
            ImageUrl = g.ImageUrl,
            Icon = g.Icon,
            Gradient = g.Gradient,
            Category = "giftcards",
            CategoryDisplay = "Подарочная карта",
            DetailUrl = $"/Catalog/GiftCard/{g.Slug}",
            ProductCount = g.Products.Count,
            MinPrice = g.Products.Any() ? g.Products.Min(p => p.Price) : null
        }));

        // VPN Providers
        var vpnProviders = await _context.VpnProviders
            .Include(v => v.Products.Where(p => p.IsActive && p.StockQuantity > 0))
            .Where(v => v.IsActive)
            .ToListAsync();

        items.AddRange(vpnProviders.Select(v => new CatalogItemViewModel
        {
            Id = v.Id,
            Name = v.Name,
            Slug = v.Slug,
            ImageUrl = v.ImageUrl,
            Icon = v.Icon,
            Gradient = v.Gradient,
            Category = "vpn",
            CategoryDisplay = "VPN",
            DetailUrl = $"/Catalog/VpnProvider/{v.Slug}",
            ProductCount = v.Products.Count,
            MinPrice = v.Products.Any() ? v.Products.Min(p => p.Price) : null
        }));

        // Shuffle
        var rng = new Random();
        items = items.OrderBy(_ => rng.Next()).ToList();

        var banners = await _context.HeroBanners
            .Where(b => b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        var model = new HomeViewModel
        {
            FeaturedItems = items,
            Banners = banners,
            TotalProducts = await _context.GameProducts.CountAsync(p => p.IsActive),
            TotalUsers = await _context.Users.CountAsync()
        };

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
