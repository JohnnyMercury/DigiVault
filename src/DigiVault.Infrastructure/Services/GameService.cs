using DigiVault.Core.Entities;
using DigiVault.Core.Interfaces;
using DigiVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DigiVault.Infrastructure.Services;

public class GameService : IGameService
{
    private readonly ApplicationDbContext _context;

    public GameService(ApplicationDbContext context)
    {
        _context = context;
    }

    #region Games

    public async Task<List<Game>> GetAllGamesAsync(bool includeInactive = false)
    {
        var query = _context.Games.AsQueryable();

        if (!includeInactive)
            query = query.Where(g => g.IsActive);

        return await query
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<Game?> GetGameByIdAsync(int id)
    {
        return await _context.Games
            .Include(g => g.Products.Where(p => p.IsActive))
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<Game?> GetGameBySlugAsync(string slug)
    {
        return await _context.Games
            .Include(g => g.Products.Where(p => p.IsActive).OrderBy(p => p.SortOrder))
            .FirstOrDefaultAsync(g => g.Slug == slug.ToLower());
    }

    public async Task<Game> CreateGameAsync(Game game)
    {
        game.Slug = game.Slug.ToLower();
        game.CreatedAt = DateTime.UtcNow;

        _context.Games.Add(game);
        await _context.SaveChangesAsync();

        return game;
    }

    public async Task<Game> UpdateGameAsync(Game game)
    {
        game.Slug = game.Slug.ToLower();

        _context.Games.Update(game);
        await _context.SaveChangesAsync();

        return game;
    }

    public async Task DeleteGameAsync(int id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game != null)
        {
            game.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }

    #endregion

    #region Game Products

    public async Task<List<GameProduct>> GetProductsByGameIdAsync(int gameId, GameProductType? type = null)
    {
        var query = _context.GameProducts
            .Where(p => p.GameId == gameId && p.IsActive);

        if (type.HasValue)
            query = query.Where(p => p.ProductType == type.Value);

        return await query
            .OrderBy(p => p.ProductType)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Price)
            .ToListAsync();
    }

    public async Task<List<GameProduct>> GetProductsByGameSlugAsync(string slug, GameProductType? type = null)
    {
        var game = await _context.Games.FirstOrDefaultAsync(g => g.Slug == slug.ToLower());
        if (game == null)
            return new List<GameProduct>();

        return await GetProductsByGameIdAsync(game.Id, type);
    }

    public async Task<GameProduct?> GetProductByIdAsync(int id)
    {
        return await _context.GameProducts
            .Include(p => p.Game)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<GameProduct> CreateProductAsync(GameProduct product)
    {
        product.CreatedAt = DateTime.UtcNow;

        _context.GameProducts.Add(product);
        await _context.SaveChangesAsync();

        return product;
    }

    public async Task<GameProduct> UpdateProductAsync(GameProduct product)
    {
        product.UpdatedAt = DateTime.UtcNow;

        _context.GameProducts.Update(product);
        await _context.SaveChangesAsync();

        return product;
    }

    public async Task DeleteProductAsync(int id)
    {
        var product = await _context.GameProducts.FindAsync(id);
        if (product != null)
        {
            product.IsActive = false;
            await _context.SaveChangesAsync();
        }
    }

    #endregion

    #region Seed Data

    public async Task SeedDefaultGamesAsync()
    {
        if (await _context.Games.AnyAsync())
            return;

        var games = new List<Game>
        {
            new Game
            {
                Name = "Fortnite",
                Slug = "fortnite",
                Currency = "V-Bucks",
                CurrencyShort = "V-Bucks",
                Icon = "🎮",
                Color = "#2d5a87",
                Gradient = "linear-gradient(135deg, #1e3a5f 0%, #2d5a87 100%)",
                ImageUrl = "/images/games/fortnite.svg",
                SortOrder = 1
            },
            new Game
            {
                Name = "Roblox",
                Slug = "roblox",
                Currency = "Robux",
                CurrencyShort = "Robux",
                Icon = "🎲",
                Color = "#e74c3c",
                Gradient = "linear-gradient(135deg, #c4281c 0%, #e74c3c 100%)",
                ImageUrl = "/images/games/roblox.svg",
                SortOrder = 2
            },
            new Game
            {
                Name = "PUBG Mobile",
                Slug = "pubg",
                Currency = "Unknown Cash",
                CurrencyShort = "UC",
                Icon = "🔫",
                Color = "#e67e22",
                Gradient = "linear-gradient(135deg, #f39c12 0%, #e67e22 100%)",
                ImageUrl = "/images/games/pubg.svg",
                SortOrder = 3
            },
            new Game
            {
                Name = "Genshin Impact",
                Slug = "genshin",
                Currency = "Genesis Crystals",
                CurrencyShort = "Crystals",
                Icon = "⚔️",
                Color = "#8b5cf6",
                Gradient = "linear-gradient(135deg, #5a3e7a 0%, #8b5cf6 100%)",
                ImageUrl = "/images/games/genshin.svg",
                SortOrder = 4
            },
            new Game
            {
                Name = "Honkai Star Rail",
                Slug = "honkai",
                Subtitle = "Star Rail",
                Currency = "Oneiric Shards",
                CurrencyShort = "Shards",
                Icon = "🌟",
                Color = "#6366f1",
                Gradient = "linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%)",
                ImageUrl = "/images/games/honkai.svg",
                SortOrder = 5
            },
            new Game
            {
                Name = "Mobile Legends",
                Slug = "mobilelegends",
                Subtitle = "Bang Bang",
                Currency = "Diamonds",
                CurrencyShort = "Diamonds",
                Icon = "💎",
                Color = "#3949ab",
                Gradient = "linear-gradient(135deg, #1a237e 0%, #3949ab 100%)",
                ImageUrl = "/images/games/mobilelegends.svg",
                SortOrder = 6
            }
        };

        _context.Games.AddRange(games);
        await _context.SaveChangesAsync();

        // Seed Fortnite products
        var fortnite = games.First(g => g.Slug == "fortnite");
        var fortniteProducts = new List<GameProduct>
        {
            new() { GameId = fortnite.Id, Name = "200 V-Bucks", Amount = "200", TotalDisplay = "200 V-Bucks", Price = 109, ProductType = GameProductType.Currency, SortOrder = 1 },
            new() { GameId = fortnite.Id, Name = "500 V-Bucks", Amount = "500", TotalDisplay = "500 V-Bucks", Price = 273, ProductType = GameProductType.Currency, SortOrder = 2 },
            new() { GameId = fortnite.Id, Name = "1000 V-Bucks", Amount = "1000", TotalDisplay = "1 000 V-Bucks", Price = 559, ProductType = GameProductType.Currency, SortOrder = 3 },
            new() { GameId = fortnite.Id, Name = "2800 V-Bucks", Amount = "2800", TotalDisplay = "2 800 V-Bucks", Price = 1444, ProductType = GameProductType.Currency, SortOrder = 4 },
            new() { GameId = fortnite.Id, Name = "5000 V-Bucks", Amount = "5000", TotalDisplay = "5 000 V-Bucks", Price = 2264, ProductType = GameProductType.Currency, SortOrder = 5 },
            new() { GameId = fortnite.Id, Name = "13500 V-Bucks", Amount = "13500", TotalDisplay = "13 500 V-Bucks", Price = 5550, ProductType = GameProductType.Currency, SortOrder = 6 }
        };

        // Seed Roblox products
        var roblox = games.First(g => g.Slug == "roblox");
        var robloxProducts = new List<GameProduct>
        {
            new() { GameId = roblox.Id, Name = "100 Robux", Amount = "100", TotalDisplay = "100 Робуксов", Price = 267, ProductType = GameProductType.Currency, SortOrder = 1 },
            new() { GameId = roblox.Id, Name = "400 Robux", Amount = "400", TotalDisplay = "400 Робуксов", Price = 489, ProductType = GameProductType.Currency, SortOrder = 2 },
            new() { GameId = roblox.Id, Name = "800 Robux", Amount = "800", TotalDisplay = "800 Робуксов", Price = 815, ProductType = GameProductType.Currency, SortOrder = 3 },
            new() { GameId = roblox.Id, Name = "1700 Robux", Amount = "1700", TotalDisplay = "1700 Робуксов", Price = 1976, ProductType = GameProductType.Currency, SortOrder = 4 },
            new() { GameId = roblox.Id, Name = "4500 Robux", Amount = "4500", TotalDisplay = "4500 Робуксов", Price = 4110, ProductType = GameProductType.Currency, SortOrder = 5 },
            new() { GameId = roblox.Id, Name = "10000 Robux", Amount = "10000", TotalDisplay = "10000 Робуксов", Price = 8243, ProductType = GameProductType.Currency, SortOrder = 6 }
        };

        // Seed PUBG products
        var pubg = games.First(g => g.Slug == "pubg");
        var pubgProducts = new List<GameProduct>
        {
            new() { GameId = pubg.Id, Name = "60 UC", Amount = "57", Bonus = "+3", TotalDisplay = "60 UC", Price = 73, OldPrice = 132, Discount = 45, ProductType = GameProductType.Currency, SortOrder = 1 },
            new() { GameId = pubg.Id, Name = "325 UC", Amount = "300", Bonus = "+25", TotalDisplay = "325 UC", Price = 376, OldPrice = 683, Discount = 45, ProductType = GameProductType.Currency, SortOrder = 2 },
            new() { GameId = pubg.Id, Name = "660 UC", Amount = "600", Bonus = "+60", TotalDisplay = "660 UC", Price = 741, OldPrice = 1140, Discount = 35, ProductType = GameProductType.Currency, SortOrder = 3 },
            new() { GameId = pubg.Id, Name = "1800 UC", Amount = "1500", Bonus = "+300", TotalDisplay = "1800 UC", Price = 2077, OldPrice = 3461, Discount = 40, ProductType = GameProductType.Currency, SortOrder = 4 },
            new() { GameId = pubg.Id, Name = "3850 UC", Amount = "3000", Bonus = "+850", TotalDisplay = "3850 UC", Price = 4129, OldPrice = 6881, Discount = 40, ProductType = GameProductType.Currency, SortOrder = 5 }
        };

        // Seed Genshin products
        var genshin = games.First(g => g.Slug == "genshin");
        var genshinProducts = new List<GameProduct>
        {
            new() { GameId = genshin.Id, Name = "60 Crystals", Amount = "60", TotalDisplay = "60 Кристаллов", Price = 53, OldPrice = 78, Discount = 32, ProductType = GameProductType.Currency, SortOrder = 1 },
            new() { GameId = genshin.Id, Name = "330 Crystals", Amount = "300", Bonus = "+30", TotalDisplay = "330 Кристаллов", Price = 269, OldPrice = 397, Discount = 32, ProductType = GameProductType.Currency, SortOrder = 2 },
            new() { GameId = genshin.Id, Name = "1090 Crystals", Amount = "980", Bonus = "+110", TotalDisplay = "1090 Кристаллов", Price = 832, OldPrice = 1232, Discount = 32, ProductType = GameProductType.Currency, SortOrder = 3 },
            new() { GameId = genshin.Id, Name = "3880 Crystals", Amount = "3280", Bonus = "+600", TotalDisplay = "3880 Кристаллов", Price = 2943, OldPrice = 4360, Discount = 33, ProductType = GameProductType.Currency, SortOrder = 4 },
            new() { GameId = genshin.Id, Name = "8080 Crystals", Amount = "6480", Bonus = "+1600", TotalDisplay = "8080 Кристаллов", Price = 5497, OldPrice = 7184, Discount = 23, ProductType = GameProductType.Currency, SortOrder = 5 },
            // Packs
            new() { GameId = genshin.Id, Name = "All Pack x2", Amount = "6480", Bonus = "+1600", TotalDisplay = "16160 Кристаллов", Price = 11069, OldPrice = 13223, Discount = 16, ProductType = GameProductType.Pack, Multiplier = "x2", SortOrder = 10 },
            // Passes
            new() { GameId = genshin.Id, Name = "Blessing", Amount = "Blessing", TotalDisplay = "Благословение полой луны", Price = 349, OldPrice = 499, Discount = 30, ProductType = GameProductType.Pass, SortOrder = 20 }
        };

        // Seed Honkai products
        var honkai = games.First(g => g.Slug == "honkai");
        var honkaiProducts = new List<GameProduct>
        {
            new() { GameId = honkai.Id, Name = "60 Shards", Amount = "60", TotalDisplay = "60 Сущностей", Price = 55, OldPrice = 88, Discount = 38, ProductType = GameProductType.Currency, SortOrder = 1 },
            new() { GameId = honkai.Id, Name = "330 Shards", Amount = "300", Bonus = "+30", TotalDisplay = "330 Сущностей", Price = 277, OldPrice = 438, Discount = 37, ProductType = GameProductType.Currency, SortOrder = 2 },
            new() { GameId = honkai.Id, Name = "1090 Shards", Amount = "980", Bonus = "+110", TotalDisplay = "1090 Сущностей", Price = 831, OldPrice = 1318, Discount = 37, ProductType = GameProductType.Currency, SortOrder = 3 },
            new() { GameId = honkai.Id, Name = "3880 Shards", Amount = "3280", Bonus = "+600", TotalDisplay = "3880 Сущностей", Price = 2961, OldPrice = 4698, Discount = 37, ProductType = GameProductType.Currency, SortOrder = 4 },
            new() { GameId = honkai.Id, Name = "8080 Shards", Amount = "6480", Bonus = "+1600", TotalDisplay = "8080 Сущностей", Price = 5355, OldPrice = 9914, Discount = 46, ProductType = GameProductType.Currency, SortOrder = 5 }
        };

        // Seed Mobile Legends products
        var ml = games.First(g => g.Slug == "mobilelegends");
        var mlProducts = new List<GameProduct>
        {
            new() { GameId = ml.Id, Name = "35 Diamonds", Amount = "32", Bonus = "+3", TotalDisplay = "35 алмазов", Price = 59, OldPrice = 90, Discount = 34, ProductType = GameProductType.Currency, SortOrder = 1 },
            new() { GameId = ml.Id, Name = "165 Diamonds", Amount = "150", Bonus = "+15", TotalDisplay = "165 алмазов", Price = 280, OldPrice = 311, Discount = 10, ProductType = GameProductType.Currency, SortOrder = 2 },
            new() { GameId = ml.Id, Name = "565 Diamonds", Amount = "500", Bonus = "+65", TotalDisplay = "565 алмазов", Price = 938, OldPrice = 1103, Discount = 15, ProductType = GameProductType.Currency, SortOrder = 3 },
            new() { GameId = ml.Id, Name = "1155 Diamonds", Amount = "1000", Bonus = "+155", TotalDisplay = "1155 алмазов", Price = 1871, OldPrice = 2201, Discount = 15, ProductType = GameProductType.Currency, SortOrder = 4 },
            new() { GameId = ml.Id, Name = "2975 Diamonds", Amount = "2500", Bonus = "+475", TotalDisplay = "2975 алмазов", Price = 4678, OldPrice = 5503, Discount = 15, ProductType = GameProductType.Currency, SortOrder = 5 },
            // Passes
            new() { GameId = ml.Id, Name = "Twilight Pass", Amount = "Twilight", TotalDisplay = "Twilight Pass", Price = 449, OldPrice = 599, Discount = 25, ProductType = GameProductType.Pass, SortOrder = 10 },
            new() { GameId = ml.Id, Name = "Starlight Member", Amount = "Starlight", TotalDisplay = "Starlight Member", Price = 549, OldPrice = 749, Discount = 27, ProductType = GameProductType.Pass, SortOrder = 11 }
        };

        _context.GameProducts.AddRange(fortniteProducts);
        _context.GameProducts.AddRange(robloxProducts);
        _context.GameProducts.AddRange(pubgProducts);
        _context.GameProducts.AddRange(genshinProducts);
        _context.GameProducts.AddRange(honkaiProducts);
        _context.GameProducts.AddRange(mlProducts);

        await _context.SaveChangesAsync();
    }

    #endregion
}
