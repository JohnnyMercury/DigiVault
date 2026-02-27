using DigiVault.Core.Enums;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class TransactionsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public TransactionsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(
        string? search,
        PaymentStatus? status,
        DateTime? from,
        DateTime? to,
        int page = 1)
    {
        var query = _context.PaymentTransactions
            .Include(t => t.User)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(t =>
                t.TransactionId.Contains(search) ||
                (t.User != null && t.User.Email != null && t.User.Email.Contains(search)));
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
            query = query.Where(t => t.CreatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.AddDays(1), DateTimeKind.Utc);
            query = query.Where(t => t.CreatedAt < toUtc);
        }

        var pageSize = 20;
        var total = await query.CountAsync();
        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        ViewBag.Total = total;

        // Статистика
        ViewBag.TotalAmount = await _context.PaymentTransactions
            .Where(t => t.Status == PaymentStatus.Completed)
            .SumAsync(t => t.Amount);

        ViewBag.TodayAmount = await _context.PaymentTransactions
            .Where(t => t.Status == PaymentStatus.Completed &&
                       t.CreatedAt >= DateTime.UtcNow.Date)
            .SumAsync(t => t.Amount);

        ViewBag.PendingCount = await _context.PaymentTransactions
            .CountAsync(t => t.Status == PaymentStatus.Pending ||
                            t.Status == PaymentStatus.Processing);

        return View(transactions);
    }

    public async Task<IActionResult> Details(int id)
    {
        var transaction = await _context.PaymentTransactions
            .Include(t => t.User)
            .Include(t => t.Order)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transaction == null)
            return NotFound();

        return View(transaction);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, PaymentStatus status)
    {
        var transaction = await _context.PaymentTransactions.FindAsync(id);
        if (transaction == null)
            return NotFound();

        transaction.Status = status;
        transaction.UpdatedAt = DateTime.UtcNow;

        if (status == PaymentStatus.Completed)
        {
            transaction.CompletedAt = DateTime.UtcNow;

            // Зачисляем средства если это пополнение
            if (transaction.OrderId == null)
            {
                var user = await _context.Users.FindAsync(transaction.UserId);
                if (user != null)
                {
                    user.Balance += transaction.Amount;
                    _context.Transactions.Add(new Core.Entities.Transaction
                    {
                        UserId = transaction.UserId,
                        Amount = transaction.Amount,
                        Type = TransactionType.Deposit,
                        Description = $"Пополнение [{transaction.TransactionId}] - ручное подтверждение"
                    });
                }
            }
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = $"Статус транзакции обновлен на {status}";
        return RedirectToAction(nameof(Details), new { id });
    }
}
