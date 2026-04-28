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
    private const int PageSize = 24;

    public CatalogController(ApplicationDbContext context, IGameService gameService, IOrderService orderService)
    {
        _context = context;
        _gameService = gameService;
        _orderService = orderService;
    }

    private async Task SetUserBalanceAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            var user = await _context.Users.FindAsync(userId);
            ViewBag.UserBalance = user?.Balance ?? 0m;
        }
    }

    /// <summary>
    /// Loads a ProductReviewsViewModel for the given product and sets it as ViewBag.Reviews.
    /// Provide exactly one of gameId/giftCardId/vpnId.
    /// productType is "Game" | "GiftCard" | "VpnProvider", productSlug is the entity slug.
    /// </summary>
    private async Task LoadReviewsViewBagAsync(string productType, string productSlug,
        int? gameId = null, int? giftCardId = null, int? vpnId = null)
    {
        IQueryable<ProductReview> query = _context.ProductReviews
            .Where(r => r.IsApproved);

        if (gameId.HasValue) query = query.Where(r => r.GameId == gameId);
        else if (giftCardId.HasValue) query = query.Where(r => r.GiftCardId == giftCardId);
        else if (vpnId.HasValue) query = query.Where(r => r.VpnProviderId == vpnId);
        else { ViewBag.Reviews = new ProductReviewsViewModel { ProductType = productType, ProductSlug = productSlug }; return; }

        var total = await query.CountAsync();
        var avg = total > 0 ? await query.AverageAsync(r => (double)r.Rating) : 0;

        var latest = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .ToListAsync();

        // Can user review? Need auth + completed order containing this product.
        // Admins bypass the purchase check (so they can author/test reviews on any product).
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        bool canReview = false;
        if (!string.IsNullOrEmpty(userId))
        {
            bool isAdmin = User.IsInRole("Admin");

            if (isAdmin)
            {
                canReview = true;
            }
            else
            {
                canReview = await _context.OrderItems
                    .Include(oi => oi.Order)
                    .Include(oi => oi.GameProduct)
                    .AnyAsync(oi => oi.Order.UserId == userId
                        && oi.Order.Status == OrderStatus.Completed
                        && oi.GameProduct != null
                        && ((gameId != null && oi.GameProduct.GameId == gameId)
                            || (giftCardId != null && oi.GameProduct.GiftCardId == giftCardId)
                            || (vpnId != null && oi.GameProduct.VpnProviderId == vpnId)));
            }

            // Exclude users who already reviewed this product (applies to admins too)
            if (canReview)
            {
                var alreadyReviewed = await _context.ProductReviews.AnyAsync(r => r.UserId == userId
                    && ((gameId != null && r.GameId == gameId)
                        || (giftCardId != null && r.GiftCardId == giftCardId)
                        || (vpnId != null && r.VpnProviderId == vpnId)));
                if (alreadyReviewed) canReview = false;
            }
        }

        ViewBag.Reviews = new ProductReviewsViewModel
        {
            Reviews = latest,
            TotalCount = total,
            AverageRating = avg,
            ProductType = productType,
            ProductSlug = productSlug,
            IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
            CanReview = canReview,
        };
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

    public async Task<IActionResult> Index(string? category = null, string? search = null, string? sort = null, int page = 1)
    {
        var items = new List<CatalogItemViewModel>();

        // Собираем родительские товары из трёх таблиц
        var showGames = string.IsNullOrWhiteSpace(category) || category == "games";
        var showGiftCards = string.IsNullOrWhiteSpace(category) || category == "giftcards";
        var showVpn = string.IsNullOrWhiteSpace(category) || category == "vpn";

        if (showGames)
        {
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
                MinPrice = g.Products.Any() ? g.Products.Min(p => p.Price) : null,
                MaxDiscount = g.Products.Any() ? g.Products.Max(p => p.Discount) : null
            }));
        }

        if (showGiftCards)
        {
            // Telegram Premium is included in the grid; its tile links to the
            // dedicated /Catalog/Telegram tab instead of the standard giftcard URL.
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
                CategoryDisplay = g.Category == GiftCardCategory.Telegram ? "Telegram" : "Подарочная карта",
                DetailUrl = g.Category == GiftCardCategory.Telegram ? "/Catalog/Telegram" : $"/Catalog/GiftCard/{g.Slug}",
                ProductCount = g.Products.Count,
                MinPrice = g.Products.Any() ? g.Products.Min(p => p.Price) : null,
                MaxDiscount = g.Products.Any() ? g.Products.Max(p => p.Discount) : null
            }));
        }

        if (showVpn)
        {
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
                MinPrice = v.Products.Any() ? v.Products.Min(p => p.Price) : null,
                MaxDiscount = v.Products.Any() ? v.Products.Max(p => p.Discount) : null
            }));
        }

        // Поиск
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            items = items.Where(i => i.Name.ToLower().Contains(searchLower)).ToList();
        }

        // Сортировка
        items = sort switch
        {
            "price_asc" => items.OrderBy(i => i.MinPrice ?? decimal.MaxValue).ToList(),
            "price_desc" => items.OrderByDescending(i => i.MinPrice ?? 0).ToList(),
            "name" => items.OrderBy(i => i.Name).ToList(),
            _ => items.OrderBy(i => i.Category).ThenBy(i => i.Name).ToList()
        };

        var totalItems = items.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var pagedItems = items
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        var model = new CatalogViewModel
        {
            Items = pagedItems,
            CategoryFilter = category,
            SearchQuery = search,
            SortBy = sort,
            CurrentPage = page,
            TotalPages = totalPages,
            TotalItems = totalItems
        };

        await SetUserBalanceAsync();
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
            await SetUserBalanceAsync();
            return View("Game", products);
        }

        // Получаем все активные игры для sidebar
        var allGames = await _gameService.GetAllGamesAsync();

        ViewBag.GameSlug = game.Slug;
        ViewBag.GameName = game.Name;
        ViewBag.Game = game;
        ViewBag.AllGames = allGames;
        await SetUserBalanceAsync();
        await LoadReviewsViewBagAsync("Game", game.Slug, gameId: game.Id);

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

        // Telegram Stars was removed entirely — fold legacy links onto the Telegram tab.
        // Telegram Premium has its own dedicated tab at /Catalog/Telegram, so route there.
        if (slug.ToLower().StartsWith("telegram"))
            return RedirectToActionPermanent("Telegram");

        var card = await _context.GiftCards
            .Include(g => g.Products.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(g => g.Slug == slug.ToLower() && g.IsActive);

        if (card == null)
            return NotFound();

        var allCards = await _context.GiftCards
            .Where(g => g.IsActive && g.Category != GiftCardCategory.Telegram)
            .OrderBy(g => g.Category)
            .ThenBy(g => g.SortOrder)
            .ToListAsync();

        ViewBag.GiftCard = card;
        ViewBag.AllGiftCards = allCards;
        await SetUserBalanceAsync();
        await LoadReviewsViewBagAsync("GiftCard", card.Slug, giftCardId: card.Id);
        return View("GiftCard");
    }

    public async Task<IActionResult> GiftCards()
    {
        // Hide Telegram-category cards from the giftcards listing —
        // they're accessed via the dedicated /Catalog/Telegram tab.
        var giftCards = await _context.GiftCards
            .Where(g => g.IsActive && g.Category != GiftCardCategory.Telegram)
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
        await SetUserBalanceAsync();
        await LoadReviewsViewBagAsync("VpnProvider", provider.Slug, vpnId: provider.Id);
        return View("VpnProvider");
    }

    /// <summary>
    /// Dedicated Telegram Premium tab. Telegram Stars was removed but this page
    /// remains as the canonical product page for Telegram Premium subscriptions.
    /// </summary>
    public async Task<IActionResult> Telegram()
    {
        var premiumCard = await _context.GiftCards
            .Include(g => g.Products.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Price))
            .FirstOrDefaultAsync(g => g.Slug == "telegram-premium" && g.IsActive);

        if (premiumCard == null)
            return NotFound();

        var premiumProducts = premiumCard.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Price)
            .ToList();

        ViewBag.PremiumCard = premiumCard;
        ViewBag.PremiumProducts = premiumProducts;
        await SetUserBalanceAsync();
        await LoadReviewsViewBagAsync("GiftCard", premiumCard.Slug, giftCardId: premiumCard.Id);

        return View("Telegram");
    }
}
