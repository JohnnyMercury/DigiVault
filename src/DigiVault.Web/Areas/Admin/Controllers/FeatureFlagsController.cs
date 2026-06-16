using DigiVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigiVault.Web.Areas.Admin.Controllers;

/// <summary>
/// Admin page with runtime visibility toggles for catalog sections.
/// Currently surfaces a single flag — VPN visibility — used by Key to
/// temporarily hide the VPN catalog while a Russian-card PSP reviews the
/// site for acquirer onboarding.
/// </summary>
public class FeatureFlagsController : AdminBaseController
{
    private readonly IFeatureFlagsService _features;

    public FeatureFlagsController(IFeatureFlagsService features)
    {
        _features = features;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        ViewBag.VpnVisible = await _features.IsVpnVisibleAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleVpn(bool visible)
    {
        await _features.SetVpnVisibleAsync(visible);
        TempData["SuccessMessage"] = visible
            ? "VPN-секция включена — категория снова видна на сайте."
            : "VPN-секция скрыта с сайта (страницы, меню, отзывы).";
        return RedirectToAction(nameof(Index));
    }
}
