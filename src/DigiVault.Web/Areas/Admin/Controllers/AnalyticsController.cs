using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class AnalyticsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public AnalyticsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var userNames = new List<string>();
        try
        {
            userNames = await _context.Users
                .Where(u => u.UserName != null && u.UserName.Trim() != "" && u.UserName.Length <= 40
                    && (u.EmailConfirmed || _context.Orders.Any(o => o.UserId == u.Id)))
                .Select(u => u.UserName!)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();
        }
        catch { }

        ViewBag.UserNames = userNames;
        return View();
    }
}
