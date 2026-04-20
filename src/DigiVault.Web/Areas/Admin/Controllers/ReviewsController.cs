using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class ReviewsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public ReviewsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search = null, string? category = null,
        int? rating = null, bool? hidden = null, int page = 1)
    {
        const int pageSize = 25;

        IQueryable<ProductReview> query = _context.ProductReviews
            .Include(r => r.Game)
            .Include(r => r.GiftCard)
            .Include(r => r.VpnProvider);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r => r.AuthorName.ToLower().Contains(s)
                || r.Title.ToLower().Contains(s)
                || r.Text.ToLower().Contains(s));
        }

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

        if (rating.HasValue) query = query.Where(r => r.Rating == rating.Value);
        if (hidden.HasValue) query = query.Where(r => r.IsApproved != hidden.Value);

        query = query.OrderByDescending(r => r.CreatedAt);

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Search = search;
        ViewBag.Category = category;
        ViewBag.Rating = rating;
        ViewBag.Hidden = hidden;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Total = total;

        // Global stats
        ViewBag.TotalAll = await _context.ProductReviews.CountAsync();
        ViewBag.TotalApproved = await _context.ProductReviews.CountAsync(r => r.IsApproved);
        ViewBag.TotalHidden = await _context.ProductReviews.CountAsync(r => !r.IsApproved);
        ViewBag.AvgRating = await _context.ProductReviews.AnyAsync()
            ? await _context.ProductReviews.Where(r => r.IsApproved).AverageAsync(r => (double)r.Rating)
            : 0;

        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var r = await _context.ProductReviews.FindAsync(id);
        if (r == null) return NotFound();
        r.IsApproved = !r.IsApproved;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = r.IsApproved ? "Отзыв опубликован" : "Отзыв скрыт";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var r = await _context.ProductReviews.FindAsync(id);
        if (r == null) return NotFound();
        _context.ProductReviews.Remove(r);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Отзыв удалён";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string? reply)
    {
        var r = await _context.ProductReviews.FindAsync(id);
        if (r == null) return NotFound();

        if (string.IsNullOrWhiteSpace(reply))
        {
            r.AdminReply = null;
            r.AdminReplyAt = null;
            TempData["SuccessMessage"] = "Ответ удалён";
        }
        else
        {
            r.AdminReply = reply.Trim();
            r.AdminReplyAt = DateTime.UtcNow;
            TempData["SuccessMessage"] = "Ответ сохранён";
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
