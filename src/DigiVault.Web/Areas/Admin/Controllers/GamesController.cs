using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Web.Areas.Admin.Controllers;

public class GamesController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IGameService _gameService;
    private readonly IFileService _fileService;
    private readonly ILogger<GamesController> _logger;

    public GamesController(ApplicationDbContext context, IGameService gameService, IFileService fileService, ILogger<GamesController> logger)
    {
        _context = context;
        _gameService = gameService;
        _fileService = fileService;
        _logger = logger;
    }

    // GET: Admin/Games
    public async Task<IActionResult> Index()
    {
        var games = await _context.Games
            .Include(g => g.Products)
            .OrderBy(g => g.SortOrder)
            .ToListAsync();

        return View(games);
    }

    // GET: Admin/Games/Create
    public IActionResult Create()
    {
        return View(new Game { SortOrder = 100 });
    }

    // POST: Admin/Games/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Game game, IFormFile? imageFile)
    {
        if (ModelState.IsValid)
        {
            if (imageFile != null)
            {
                game.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            await _gameService.CreateGameAsync(game);
            TempData["SuccessMessage"] = "Игра успешно создана";
            return RedirectToAction(nameof(Index));
        }

        return View(game);
    }

    // GET: Admin/Games/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null)
            return NotFound();

        return View(game);
    }

    // POST: Admin/Games/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Game game, IFormFile? imageFile)
    {
        if (id != game.Id)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                var existingGame = await _context.Games.FindAsync(id);
                if (existingGame == null)
                    return NotFound();

                existingGame.Name = game.Name;
                existingGame.Slug = game.Slug.ToLower();
                existingGame.Subtitle = game.Subtitle;
                existingGame.Currency = game.Currency;
                existingGame.CurrencyShort = game.CurrencyShort;
                existingGame.Icon = game.Icon;
                existingGame.Color = game.Color;
                existingGame.Gradient = game.Gradient;
                existingGame.SortOrder = game.SortOrder;
                existingGame.IsActive = game.IsActive;

                if (imageFile != null)
                {
                    if (!string.IsNullOrEmpty(existingGame.ImageUrl) && existingGame.ImageUrl.StartsWith("/images/uploads/"))
                    {
                        _fileService.DeleteImage(existingGame.ImageUrl);
                    }
                    existingGame.ImageUrl = await _fileService.SaveImageAsync(imageFile);
                }
                else if (!string.IsNullOrEmpty(game.ImageUrl))
                {
                    existingGame.ImageUrl = game.ImageUrl;
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Игра успешно обновлена";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Games.AnyAsync(g => g.Id == id))
                    return NotFound();
                throw;
            }
        }

        return View(game);
    }

    // POST: Admin/Games/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game != null)
        {
            game.IsActive = false;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Игра скрыта";
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: Admin/Games/Destroy/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Destroy(int id)
    {
        var game = await _context.Games
            .Include(g => g.Products)
                .ThenInclude(p => p.ProductKeys)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game != null)
        {
            if (!string.IsNullOrEmpty(game.ImageUrl) && game.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(game.ImageUrl);
            }
            foreach (var product in game.Products)
            {
                if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
                {
                    _fileService.DeleteImage(product.ImageUrl);
                }
            }
            _context.Games.Remove(game);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Игра полностью удалена";
        }

        return RedirectToAction(nameof(Index));
    }

    // GET: Admin/Games/Products/5
    public async Task<IActionResult> Products(int id, GameProductType? type = null)
    {
        var game = await _context.Games
            .Include(g => g.Products.OrderBy(p => p.ProductType).ThenBy(p => p.SortOrder))
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game == null)
            return NotFound();

        ViewBag.ProductType = type;
        return View(game);
    }

    // GET: Admin/Games/CreateProduct/5
    public async Task<IActionResult> CreateProduct(int id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null)
            return NotFound();

        ViewBag.Game = game;
        return View(new GameProduct { GameId = id, SortOrder = 100, IsActive = true });
    }

    // POST: Admin/Games/CreateProduct
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(GameProduct product, IFormFile? imageFile)
    {
        // Remove navigation property validation errors
        ModelState.Remove("Game");
        ModelState.Remove("ProductKeys");

        if (ModelState.IsValid)
        {
            if (imageFile != null)
            {
                product.ImageUrl = await _fileService.SaveImageAsync(imageFile);
            }

            product.CreatedAt = DateTime.UtcNow;
            _context.GameProducts.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Товар успешно создан";
            return RedirectToAction(nameof(Products), new { id = product.GameId });
        }

        var game = await _context.Games.FindAsync(product.GameId);
        ViewBag.Game = game;
        return View(product);
    }

    // GET: Admin/Games/EditProduct/5
    public async Task<IActionResult> EditProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.Game)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            return NotFound();

        ViewBag.Game = product.Game;
        return View(product);
    }

    // POST: Admin/Games/EditProduct
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProduct(int id, GameProduct product, IFormFile? imageFile)
    {
        if (id != product.Id)
            return NotFound();

        // Remove navigation property validation errors
        ModelState.Remove("Game");
        ModelState.Remove("ProductKeys");

        // Log validation errors for debugging
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .Select(x => new { Key = x.Key, Errors = x.Value?.Errors.Select(e => e.ErrorMessage).ToList() })
                .ToList();

            _logger.LogWarning("EditProduct Validation Errors: {Errors}", System.Text.Json.JsonSerializer.Serialize(errors));
        }

        if (ModelState.IsValid)
        {
            try
            {
                var existingProduct = await _context.GameProducts.FindAsync(id);
                if (existingProduct == null)
                    return NotFound();

                existingProduct.Name = product.Name;
                existingProduct.Amount = product.Amount;
                existingProduct.Bonus = product.Bonus;
                existingProduct.TotalDisplay = product.TotalDisplay;
                existingProduct.Price = product.Price;
                existingProduct.OldPrice = product.OldPrice;
                existingProduct.Discount = product.Discount;
                existingProduct.ProductType = product.ProductType;
                existingProduct.Multiplier = product.Multiplier;
                existingProduct.Region = product.Region;
                existingProduct.SortOrder = product.SortOrder;
                existingProduct.IsActive = product.IsActive;
                existingProduct.IsFeatured = product.IsFeatured;
                existingProduct.StockQuantity = product.StockQuantity;
                existingProduct.UpdatedAt = DateTime.UtcNow;

                if (imageFile != null)
                {
                    if (!string.IsNullOrEmpty(existingProduct.ImageUrl) && existingProduct.ImageUrl.StartsWith("/images/uploads/"))
                    {
                        _fileService.DeleteImage(existingProduct.ImageUrl);
                    }
                    existingProduct.ImageUrl = await _fileService.SaveImageAsync(imageFile);
                }
                else if (!string.IsNullOrEmpty(product.ImageUrl))
                {
                    existingProduct.ImageUrl = product.ImageUrl;
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Товар успешно обновлен";
                return RedirectToAction(nameof(Products), new { id = existingProduct.GameId });
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.GameProducts.AnyAsync(p => p.Id == id))
                    return NotFound();
                throw;
            }
        }

        var game = await _context.Games.FindAsync(product.GameId);
        ViewBag.Game = game;
        return View(product);
    }

    // POST: Admin/Games/DeleteProduct/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await _context.GameProducts
            .Include(p => p.ProductKeys)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product != null)
        {
            var gameId = product.GameId;
            if (!string.IsNullOrEmpty(product.ImageUrl) && product.ImageUrl.StartsWith("/images/uploads/"))
            {
                _fileService.DeleteImage(product.ImageUrl);
            }
            _context.GameProducts.Remove(product);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Товар удалён";
            return RedirectToAction(nameof(Products), new { id = gameId });
        }

        return RedirectToAction(nameof(Index));
    }

    // POST: Admin/Games/Seed
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Seed()
    {
        await _gameService.SeedDefaultGamesAsync();
        TempData["SuccessMessage"] = "Игры и товары успешно загружены";
        return RedirectToAction(nameof(Index));
    }
}
