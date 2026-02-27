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
        var featuredProducts = await _context.Products
            .Where(p => p.IsActive && p.StockQuantity > 0)
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync();

        var model = new HomeViewModel
        {
            FeaturedProducts = featuredProducts,
            TotalProducts = await _context.Products.CountAsync(p => p.IsActive),
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
