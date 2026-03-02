using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class HeroBannersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IFileService _fileService;

    public HeroBannersController(ApplicationDbContext context, IFileService fileService)
    {
        _context = context;
        _fileService = fileService;
    }

    public async Task<IActionResult> Index()
    {
        var banners = await _context.HeroBanners
            .OrderBy(b => b.SortOrder)
            .ToListAsync();

        return View(banners);
    }

    public IActionResult Create()
    {
        return View(new HeroBanner { SortOrder = 100 });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HeroBanner banner, IFormFile? imageFile)
    {
        if (ModelState.IsValid)
        {
            if (imageFile != null)
            {
                banner.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            _context.HeroBanners.Add(banner);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Баннер успешно создан";
            return RedirectToAction(nameof(Index));
        }

        return View(banner);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var banner = await _context.HeroBanners.FindAsync(id);
        if (banner == null)
            return NotFound();

        return View(banner);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, HeroBanner banner, IFormFile? imageFile)
    {
        if (id != banner.Id)
            return NotFound();

        if (ModelState.IsValid)
        {
            var existing = await _context.HeroBanners.FindAsync(id);
            if (existing == null)
                return NotFound();

            existing.Title = banner.Title;
            existing.Subtitle = banner.Subtitle;
            existing.Description = banner.Description;
            existing.ButtonText = banner.ButtonText;
            existing.ButtonUrl = banner.ButtonUrl;
            existing.Gradient = banner.Gradient;
            existing.SubtitleColor = banner.SubtitleColor;
            existing.ButtonClass = banner.ButtonClass;
            existing.SortOrder = banner.SortOrder;
            existing.IsActive = banner.IsActive;

            if (imageFile != null)
            {
                if (!string.IsNullOrEmpty(existing.ImageUrl) && existing.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(existing.ImageUrl);
                }
                existing.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }
            else if (!string.IsNullOrEmpty(banner.ImageUrl))
            {
                existing.ImageUrl = banner.ImageUrl;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Баннер обновлён";
            return RedirectToAction(nameof(Index));
        }

        return View(banner);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var banner = await _context.HeroBanners.FindAsync(id);
        if (banner != null)
        {
            banner.IsActive = !banner.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = banner.IsActive ? "Баннер включён" : "Баннер скрыт";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Destroy(int id)
    {
        var banner = await _context.HeroBanners.FindAsync(id);
        if (banner != null)
        {
            if (!string.IsNullOrEmpty(banner.ImageUrl) && banner.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(banner.ImageUrl);
            }
            _context.HeroBanners.Remove(banner);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Баннер удалён";
        }

        return RedirectToAction(nameof(Index));
    }
}
