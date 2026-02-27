using DigiVault.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class DatabaseController : AdminBaseController
{
    private readonly IDatabaseService _databaseService;

    public DatabaseController(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task<IActionResult> Index()
    {
        var dbInfo = await _databaseService.GetDatabaseInfoAsync();
        var tables = await _databaseService.GetTableNamesAsync();

        ViewBag.DatabaseInfo = dbInfo;
        ViewBag.Tables = tables;

        return View();
    }

    public async Task<IActionResult> ViewTable(string name, int page = 1)
    {
        if (string.IsNullOrEmpty(name))
            return RedirectToAction("Index");

        var data = await _databaseService.GetTableDataAsync(name, page);
        var rowCount = await _databaseService.GetTableRowCountAsync(name);
        var columns = await _databaseService.GetTableColumnsAsync(name);

        ViewBag.TableName = name;
        ViewBag.Data = data;
        ViewBag.RowCount = rowCount;
        ViewBag.Columns = columns;
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(rowCount / 50.0);

        return View();
    }

    [HttpGet]
    public IActionResult SqlQuery()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SqlQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            TempData["ErrorMessage"] = "Введите SQL запрос";
            return View();
        }

        try
        {
            var result = await _databaseService.ExecuteSelectQueryAsync(query);
            ViewBag.Query = query;
            ViewBag.Result = result;
            ViewBag.RowCount = result.Rows.Count;
            return View("QueryResult");
        }
        catch (ArgumentException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            ViewBag.Query = query;
            return View();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Ошибка выполнения запроса: {ex.Message}";
            ViewBag.Query = query;
            return View();
        }
    }
}
