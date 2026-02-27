using DigiVault.Core.Entities;

namespace DigiVault.Core.Interfaces;

public interface IGameService
{
    // Games
    Task<List<Game>> GetAllGamesAsync(bool includeInactive = false);
    Task<Game?> GetGameByIdAsync(int id);
    Task<Game?> GetGameBySlugAsync(string slug);
    Task<Game> CreateGameAsync(Game game);
    Task<Game> UpdateGameAsync(Game game);
    Task DeleteGameAsync(int id);

    // Game Products
    Task<List<GameProduct>> GetProductsByGameIdAsync(int gameId, GameProductType? type = null);
    Task<List<GameProduct>> GetProductsByGameSlugAsync(string slug, GameProductType? type = null);
    Task<GameProduct?> GetProductByIdAsync(int id);
    Task<GameProduct> CreateProductAsync(GameProduct product);
    Task<GameProduct> UpdateProductAsync(GameProduct product);
    Task DeleteProductAsync(int id);

    // Seed data
    Task SeedDefaultGamesAsync();
}
