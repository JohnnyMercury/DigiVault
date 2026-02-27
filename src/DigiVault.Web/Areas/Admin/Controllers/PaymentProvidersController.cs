using DigiVault.Core.Entities;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class PaymentProvidersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public PaymentProvidersController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var providers = await _context.PaymentProviderConfigs
            .OrderBy(p => p.Priority)
            .ToListAsync();

        // Get transaction stats per provider
        var stats = await _context.PaymentTransactions
            .GroupBy(t => t.ProviderName)
            .Select(g => new
            {
                Provider = g.Key,
                Total = g.Count(),
                Successful = g.Count(t => t.Status == Core.Enums.PaymentStatus.Completed),
                Revenue = g.Where(t => t.Status == Core.Enums.PaymentStatus.Completed).Sum(t => t.Amount)
            })
            .ToDictionaryAsync(x => x.Provider, x => new { x.Total, x.Successful, x.Revenue });

        ViewBag.Stats = stats;
        return View(providers);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var provider = await _context.PaymentProviderConfigs.FindAsync(id);
        if (provider == null)
        {
            TempData["ErrorMessage"] = "Провайдер не найден";
            return RedirectToAction("Index");
        }
        return View(provider);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PaymentProviderConfig model)
    {
        var provider = await _context.PaymentProviderConfigs.FindAsync(id);
        if (provider == null)
        {
            TempData["ErrorMessage"] = "Провайдер не найден";
            return RedirectToAction("Index");
        }

        provider.DisplayName = model.DisplayName;
        provider.IsEnabled = model.IsEnabled;
        provider.IsTestMode = model.IsTestMode;
        provider.Priority = model.Priority;
        provider.ApiKey = model.ApiKey;
        provider.SecretKey = model.SecretKey;
        provider.MerchantId = model.MerchantId;
        provider.Commission = model.Commission;
        provider.MinAmount = model.MinAmount;
        provider.MaxAmount = model.MaxAmount;
        provider.Settings = model.Settings;
        provider.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Провайдер {provider.DisplayName} обновлён";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var provider = await _context.PaymentProviderConfigs.FindAsync(id);
        if (provider == null)
        {
            TempData["ErrorMessage"] = "Провайдер не найден";
            return RedirectToAction("Index");
        }

        provider.IsEnabled = !provider.IsEnabled;
        provider.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{provider.DisplayName}: {(provider.IsEnabled ? "включён" : "выключен")}";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new PaymentProviderConfig { Name = "", DisplayName = "" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PaymentProviderConfig model)
    {
        if (await _context.PaymentProviderConfigs.AnyAsync(p => p.Name == model.Name))
        {
            TempData["ErrorMessage"] = $"Провайдер с именем {model.Name} уже существует";
            return View(model);
        }

        _context.PaymentProviderConfigs.Add(model);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Провайдер {model.DisplayName} создан";
        return RedirectToAction("Index");
    }
}
