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
        var realUsers = new List<string>();
        try
        {
            realUsers = await _context.Users
                .Where(u => u.UserName != null && u.UserName.Trim() != "" && u.UserName.Length <= 40
                    && (u.EmailConfirmed || _context.Orders.Any(o => o.UserId == u.Id)))
                .Select(u => u.UserName!)
                .Distinct()
                .ToListAsync();
        }
        catch { }

        // Mix real DB users with demo nicknames (we only have a couple of real users, so
        // the live-feed would feel empty otherwise). Demo names are in DemoUsernames.
        ViewBag.UserNames = DemoUsernames.Merge(realUsers).ToList();
        return View();
    }
}
