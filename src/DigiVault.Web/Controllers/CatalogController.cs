using System.Security.Claims;
using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using DigiVault.Web.Models;
using DigiVault.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Controllers;

public class PurchaseRequest
{
    public int GameProductId { get; set; }
    public string? DeliveryInfo { get; set; }
    public string PaymentMethod { get; set; } = "balance";
}

public class CatalogController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IGameService _gameService;
    private readonly IOrderService _orderService;
    private const int PageSize = 12;

    public CatalogController(ApplicationDbContext context, IGameService gameService, IOrderService orderService)
    {
        _context = context;
        _gameService = gameService;
        _orderService = orderService;
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Purchase([FromBody] PurchaseRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Json(new PurchaseResult { Success = false, ErrorMessage = "Необходимо войти в аккаунт" });

        if (request.PaymentMethod != "balance")
            return Json(new PurchaseResult { Success = false, ErrorMessage = "Пока доступна только оплата с баланса" });

        var result = await _orderService.CreatePurchaseAsync(userId, request.GameProductId, 1, request.DeliveryInfo);
        return Json(result);
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
            _ => query.OrderByDescending(p => p.IsFeatured).ThenByDescending(p => p.CreatedAt)
        };

        var totalProducts = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalProducts / (double)PageSize);

        var products = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var model = new CatalogViewModel
        {
            Products = products,
            SelectedCategory = category,
            SearchQuery = search,
            SortBy = sort,
            CurrentPage = page,
            TotalPages = totalPages
        };

        // Получаем игры для отображения на главной каталога
        var games = await _gameService.GetAllGamesAsync();
        ViewBag.Games = games;

        return View(model);
    }

    public async Task<IActionResult> Product(int id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);

        if (product == null)
            return NotFound();

        return View(product);
    }

    public async Task<IActionResult> GameCurrency()
    {
        // Получаем все игры из базы данных
        var games = await _gameService.GetAllGamesAsync();
        ViewBag.Games = games;
        return View("GameCurrency");
    }

    public async Task<IActionResult> Game(string slug)
    {
        // Страница конкретной игры с вариантами покупки
        if (string.IsNullOrEmpty(slug))
            return NotFound();

        // Получаем игру из базы данных
        var game = await _gameService.GetGameBySlugAsync(slug);

        if (game == null)
        {
            // Fallback на старую логику если игра не в базе
            var searchTerm = slug.ToLower();
            var products = await _context.Products
                .Where(p => p.IsActive && p.Category == ProductCategory.GameCurrency)
                .Where(p => p.Name.ToLower().Contains(searchTerm) ||
                           p.Description.ToLower().Contains(searchTerm))
                .OrderBy(p => p.Price)
                .ToListAsync();

            ViewBag.GameSlug = slug;
            ViewBag.GameName = GetGameDisplayName(slug);
            ViewBag.Game = null;
            return View("Game", products);
        }

        // Получаем все активные игры для sidebar
        var allGames = await _gameService.GetAllGamesAsync();

        ViewBag.GameSlug = game.Slug;
        ViewBag.GameName = game.Name;
        ViewBag.Game = game;
        ViewBag.AllGames = allGames;

        return View("Game", new List<Product>());
    }

    private string GetGameDisplayName(string slug)
    {
        return slug.ToLower() switch
        {
            "fortnite" => "Fortnite",
            "roblox" => "Roblox",
            "pubg" => "PUBG Mobile",
            "brawlstars" or "brawl" => "Brawl Stars",
            "genshin" => "Genshin Impact",
            "minecraft" => "Minecraft",
            "clash" or "clashroyale" => "Clash Royale",
            "playstation" or "psn" => "PlayStation",
            "xbox" => "Xbox",
            _ => slug
        };
    }

    public async Task<IActionResult> GiftCard(string slug)
    {
        if (string.IsNullOrEmpty(slug))
            return NotFound();

        var card = await _context.GiftCards
            .Include(g => g.Products.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(g => g.Slug == slug.ToLower() && g.IsActive);

        if (card == null)
            return NotFound();

        var allCards = await _context.GiftCards
            .Where(g => g.IsActive)
            .OrderBy(g => g.Category)
            .ThenBy(g => g.SortOrder)
            .ToListAsync();

        ViewBag.GiftCard = card;
        ViewBag.AllGiftCards = allCards;
        return View("GiftCard");
    }

    public async Task<IActionResult> GiftCards()
    {
        var giftCards = await _context.GiftCards
            .Where(g => g.IsActive)
            .OrderBy(g => g.Category)
            .ThenBy(g => g.SortOrder)
            .ToListAsync();

        ViewBag.GiftCards = giftCards;
        return View("GiftCards");
    }

    public async Task<IActionResult> Vpn()
    {
        var vpnProviders = await _context.VpnProviders
            .Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder)
            .ToListAsync();

        ViewBag.VpnProviders = vpnProviders;
        return View("Vpn");
    }

    public async Task<IActionResult> VpnProvider(string slug)
    {
        if (string.IsNullOrEmpty(slug))
            return NotFound();

        var provider = await _context.VpnProviders
            .Include(v => v.Products.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(v => v.Slug == slug.ToLower() && v.IsActive);

        if (provider == null)
            return NotFound();

        var allProviders = await _context.VpnProviders
            .Where(v => v.IsActive)
            .OrderBy(v => v.SortOrder)
            .ToListAsync();

        ViewBag.VpnProvider = provider;
        ViewBag.AllVpnProviders = allProviders;
        return View("VpnProvider");
    }
}
