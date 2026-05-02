using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DigiVault.Web.Controllers;

public class ReviewsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReviewsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET /Reviews
    [HttpGet]
    public async Task<IActionResult> Index(string? category = null, int? rating = null,
        string sort = "newest", int page = 1, string? product = null)
    {
        const int pageSize = 10;

        IQueryable<ProductReview> query = _context.ProductReviews
            .Include(r => r.Game)
            .Include(r => r.GiftCard)
            .Include(r => r.VpnProvider)
            .Where(r => r.IsApproved);

        // Filter by category
        if (!string.IsNullOrEmpty(category))
        {
            query = category switch
            {
                "games" => query.Where(r => r.GameId != null),
                "telegram" => query.Where(r => r.GiftCard != null && r.GiftCard.Category == GiftCardCategory.Telegram),
                "giftcards" => query.Where(r => r.GiftCardId != null && r.GiftCard!.Category != GiftCardCategory.Telegram),
                "vpn" => query.Where(r => r.VpnProviderId != null),
                _ => query
            };
        }

        // Filter by rating
        if (rating.HasValue && rating.Value >= 1 && rating.Value <= 5)
        {
            query = query.Where(r => r.Rating == rating.Value);
        }

        // Filter by specific product slug.
        // Telegram Stars was removed from the catalog by SB request — block any
        // direct ?product=telegram-stars deep-link so the slug doesn't surface
        // a public, indexable page of historical reviews.
        if (!string.IsNullOrEmpty(product))
        {
            if (product.Equals("telegram-stars", StringComparison.OrdinalIgnoreCase))
                return NotFound();

            query = query.Where(r =>
                (r.Game != null && r.Game.Slug == product) ||
                (r.GiftCard != null && r.GiftCard.Slug == product) ||
                (r.VpnProvider != null && r.VpnProvider.Slug == product));
        }

        // Sort
        query = sort switch
        {
            "oldest" => query.OrderBy(r => r.CreatedAt),
            "helpful" => query.OrderByDescending(r => r.HelpfulCount),
            "rating_high" => query.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "rating_low" => query.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            _ => query.OrderByDescending(r => r.CreatedAt)
        };

        // Stats across ALL approved reviews (not affected by filters)
        var allApprovedQuery = _context.ProductReviews
            .Include(r => r.GiftCard)
            .Where(r => r.IsApproved);

        var stats = new ReviewStats
        {
            TotalCount = await allApprovedQuery.CountAsync(),
            AverageRating = await allApprovedQuery.AnyAsync() ? await allApprovedQuery.AverageAsync(r => (double)r.Rating) : 0,
            RatingCounts = await allApprovedQuery
                .GroupBy(r => r.Rating)
                .Select(g => new { Rating = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Rating, g => g.Count),
        };

        int totalFiltered = await query.CountAsync();
        int totalPages = (int)Math.Ceiling(totalFiltered / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Check if current user can write reviews (has any completed order OR is admin)
        var userId = _userManager.GetUserId(User);
        bool hasPurchase = false;
        if (userId != null)
        {
            hasPurchase = User.IsInRole("Admin") || await _context.Orders
                .AnyAsync(o => o.UserId == userId && o.Status == OrderStatus.Completed);
        }

        ViewBag.Stats = stats;
        ViewBag.Category = category;
        ViewBag.Rating = rating;
        ViewBag.Sort = sort;
        ViewBag.Product = product;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalFiltered = totalFiltered;
        ViewBag.IsAuthenticated = User.Identity?.IsAuthenticated ?? false;
        ViewBag.HasPurchase = hasPurchase;

        return View(items);
    }

    // POST /Reviews/MarkHelpful
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkHelpful(int id)
    {
        var review = await _context.ProductReviews.FindAsync(id);
        if (review == null) return NotFound();

        review.HelpfulCount++;
        await _context.SaveChangesAsync();
        return Json(new { count = review.HelpfulCount });
    }

    // POST /Reviews/Create — submit new review
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string productType, string productSlug, int rating,
        string title, string text)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        // Validate rating
        if (rating < 1 || rating > 5)
        {
            TempData["ErrorMessage"] = "Неверный рейтинг";
            return RedirectToReferer(productType, productSlug);
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text))
        {
            TempData["ErrorMessage"] = "Заполните заголовок и текст отзыва";
            return RedirectToReferer(productType, productSlug);
        }

        // Find product
        int? gameId = null, giftCardId = null, vpnId = null;
        switch (productType?.ToLower())
        {
            case "game":
                var game = await _context.Games.FirstOrDefaultAsync(g => g.Slug == productSlug);
                if (game == null) return NotFound();
                gameId = game.Id;
                break;
            case "giftcard":
                var gc = await _context.GiftCards.FirstOrDefaultAsync(g => g.Slug == productSlug);
                if (gc == null) return NotFound();
                giftCardId = gc.Id;
                break;
            case "vpnprovider":
                var vpn = await _context.VpnProviders.FirstOrDefaultAsync(v => v.Slug == productSlug);
                if (vpn == null) return NotFound();
                vpnId = vpn.Id;
                break;
            default:
                return BadRequest();
        }

        // Verify purchase: user must have a completed order containing this specific product.
        // Admins bypass this check for demo/testing purposes.
        bool isAdmin = User.IsInRole("Admin");
        if (!isAdmin)
        {
            var hasPurchased = await _context.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.GameProduct)
                .AnyAsync(oi => oi.Order.UserId == user.Id
                    && oi.Order.Status == OrderStatus.Completed
                    && oi.GameProduct != null
                    && (gameId != null && oi.GameProduct.GameId == gameId
                        || giftCardId != null && oi.GameProduct.GiftCardId == giftCardId
                        || vpnId != null && oi.GameProduct.VpnProviderId == vpnId));

            if (!hasPurchased)
            {
                TempData["ErrorMessage"] = "Оставить отзыв можно только после покупки этого товара";
                return RedirectToReferer(productType, productSlug);
            }
        }

        // Check for duplicate review from this user for this product
        var existing = await _context.ProductReviews.AnyAsync(r => r.UserId == user.Id
            && (gameId != null && r.GameId == gameId
                || giftCardId != null && r.GiftCardId == giftCardId
                || vpnId != null && r.VpnProviderId == vpnId));

        if (existing)
        {
            TempData["ErrorMessage"] = "Вы уже оставляли отзыв на этот товар";
            return RedirectToReferer(productType, productSlug);
        }

        var review = new ProductReview
        {
            UserId = user.Id,
            AuthorName = user.UserName ?? "Пользователь",
            GameId = gameId,
            GiftCardId = giftCardId,
            VpnProviderId = vpnId,
            Rating = rating,
            Title = title.Trim().Length > 200 ? title.Trim().Substring(0, 200) : title.Trim(),
            Text = text.Trim().Length > 2000 ? text.Trim().Substring(0, 2000) : text.Trim(),
            IsVerifiedPurchase = true,
            IsApproved = true,
            CreatedAt = DateTime.UtcNow,
        };

        _context.ProductReviews.Add(review);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Спасибо за отзыв!";
        return RedirectToReferer(productType, productSlug);
    }

    private IActionResult RedirectToReferer(string? productType, string? productSlug)
    {
        return productType?.ToLower() switch
        {
            "game" => RedirectToAction("Game", "Catalog", new { slug = productSlug }),
            "giftcard" => RedirectToAction("GiftCard", "Catalog", new { slug = productSlug }),
            "vpnprovider" => RedirectToAction("VpnProvider", "Catalog", new { slug = productSlug }),
            _ => RedirectToAction(nameof(Index))
        };
    }
}

public class ReviewStats
{
    public int TotalCount { get; set; }
    public double AverageRating { get; set; }
    public Dictionary<int, int> RatingCounts { get; set; } = new();

    public int GetCount(int rating) => RatingCounts.TryGetValue(rating, out var c) ? c : 0;
    public double GetPercent(int rating) => TotalCount > 0 ? GetCount(rating) * 100.0 / TotalCount : 0;

    /// <summary>% of reviews that are 4+ stars — "рекомендуют нас"</summary>
    public double RecommendPercent => TotalCount > 0
        ? (GetCount(5) + GetCount(4)) * 100.0 / TotalCount
        : 0;
}
