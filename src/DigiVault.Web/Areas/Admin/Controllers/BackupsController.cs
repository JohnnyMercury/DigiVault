using DigiVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class BackupsController : AdminBaseController
{
    private readonly IBackupService _backupService;

    public BackupsController(IBackupService backupService)
    {
        _backupService = backupService;
    }

    public async Task<IActionResult> Index()
    {
        var backups = await _backupService.GetBackupHistoryAsync();
        return View(backups);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create()
    {
        try
        {
            var backupId = await _backupService.CreateFullBackupAsync();
            TempData["SuccessMessage"] = $"Бэкап создан: {backupId}";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Ошибка создания бэкапа: {ex.Message}";
        }
        return RedirectToAction("Index");
    }

    public async Task<IActionResult> Download(string id)
    {
        var path = await _backupService.GetBackupFilePathAsync(id);
        if (path == null)
        {
            TempData["ErrorMessage"] = "Бэкап не найден";
            return RedirectToAction("Index");
        }

        var fileName = Path.GetFileName(path);
        return PhysicalFile(path, "application/zip", fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (await _backupService.DeleteBackupAsync(id))
            TempData["SuccessMessage"] = "Бэкап удалён";
        else
            TempData["ErrorMessage"] = "Бэкап не найден";

        return RedirectToAction("Index");
    }
}
