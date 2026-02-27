using DigiVault.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DigiVault.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await context.Database.MigrateAsync();

        // Seed roles
        var roles = new[] { "Admin", "User" };
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Seed admin user
        var adminEmail = "admin@digivault.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                Balance = 1000,
                IsActive = true
            };
            await userManager.CreateAsync(adminUser, "Admin123!");
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }

        // Seed gift cards
        if (!await context.GiftCards.AnyAsync())
        {
            context.GiftCards.AddRange(
                new GiftCard { Name = "PlayStation Store", Slug = "psn", Description = "PSN карты и PS Plus", Icon = "🎮", ImageUrl = "/images/products/psn.svg", Gradient = "linear-gradient(135deg, #003791, #0070d1)", Category = GiftCardCategory.Gaming, SortOrder = 1 },
                new GiftCard { Name = "Xbox", Slug = "xbox", Description = "Game Pass и подарочные карты", Icon = "🎮", ImageUrl = "/images/products/xbox.svg", Gradient = "linear-gradient(135deg, #107c10, #1db954)", Category = GiftCardCategory.Gaming, SortOrder = 3 },
                new GiftCard { Name = "Nintendo eShop", Slug = "nintendo", Description = "Карты для Nintendo Switch", Icon = "🕹️", ImageUrl = "/images/products/nintendo.svg", Gradient = "linear-gradient(135deg, #e60012, #c80010)", Category = GiftCardCategory.Gaming, SortOrder = 4 },
                new GiftCard { Name = "Netflix", Slug = "netflix", Description = "Подарочные карты Netflix", Icon = "🎬", ImageUrl = "/images/products/netflix.svg", Gradient = "linear-gradient(135deg, #8b0000, #e50914)", Category = GiftCardCategory.Streaming, SortOrder = 1 },
                new GiftCard { Name = "Spotify", Slug = "spotify", Description = "Premium подписка", Icon = "🎵", ImageUrl = "/images/products/spotify.svg", Gradient = "linear-gradient(135deg, #121212, #1db954)", Category = GiftCardCategory.Streaming, SortOrder = 2 },
                new GiftCard { Name = "Apple / iTunes", Slug = "apple", Description = "App Store и Apple Music", Icon = "🍎", ImageUrl = "/images/products/apple.svg", Gradient = "linear-gradient(135deg, #fb5b89, #d636ba)", Category = GiftCardCategory.Streaming, SortOrder = 3 },
                new GiftCard { Name = "YouTube Premium", Slug = "youtube", Description = "Без рекламы + YouTube Music", Icon = "▶️", ImageUrl = "/images/products/youtube.svg", Gradient = "linear-gradient(135deg, #282828, #ff0000)", Category = GiftCardCategory.Streaming, SortOrder = 4 }
            );
            await context.SaveChangesAsync();
        }

        // Seed PSN gift card products
        var psnCard = await context.GiftCards.FirstOrDefaultAsync(g => g.Slug == "psn");
        if (psnCard != null && !await context.GameProducts.AnyAsync(p => p.GiftCardId == psnCard.Id))
        {
            var psnProducts = new[]
            {
                new GameProduct { GiftCardId = psnCard.Id, Name = "1 USD", Amount = "1 USD", TotalDisplay = "PS Store USA 1$", Price = 100, OldPrice = 125, Discount = 20, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 1, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "2 USD", Amount = "2 USD", TotalDisplay = "PS Store USA 2$", Price = 206, OldPrice = 257, Discount = 20, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 2, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "3 USD", Amount = "3 USD", TotalDisplay = "PS Store USA 3$", Price = 292, OldPrice = 343, Discount = 15, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 3, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "4 USD", Amount = "4 USD", TotalDisplay = "PS Store USA 4$", Price = 411, OldPrice = 483, Discount = 15, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 4, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "5 USD", Amount = "5 USD", TotalDisplay = "PS Store USA 5$", Price = 514, OldPrice = 604, Discount = 15, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 5, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "10 USD", Amount = "10 USD", TotalDisplay = "PS Store USA 10$", Price = 924, OldPrice = 1320, Discount = 30, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 6, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "15 USD", Amount = "15 USD", TotalDisplay = "PS Store USA 15$", Price = 1455, OldPrice = 1940, Discount = 25, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 7, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "25 USD", Amount = "25 USD", TotalDisplay = "PS Store USA 25$", Price = 2310, OldPrice = 3300, Discount = 30, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 8, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "20 USD", Amount = "20 USD", TotalDisplay = "PS Store USA 20$", Price = 2399, OldPrice = 3198, Discount = 25, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 9, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "30 USD", Amount = "30 USD", TotalDisplay = "PS Store USA 30$", Price = 2772, OldPrice = 3696, Discount = 25, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 10, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "35 USD", Amount = "35 USD", TotalDisplay = "PS Store USA 35$", Price = 3215, OldPrice = 4286, Discount = 25, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 11, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "45 USD", Amount = "45 USD", TotalDisplay = "PS Store USA 45$", Price = 4303, OldPrice = 4781, Discount = 10, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 12, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "50 USD", Amount = "50 USD", TotalDisplay = "PS Store USA 50$", Price = 4619, OldPrice = 5132, Discount = 10, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 13, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "60 USD", Amount = "60 USD", TotalDisplay = "PS Store USA 60$", Price = 5543, OldPrice = 6834, Discount = 19, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 14, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "70 USD", Amount = "70 USD", TotalDisplay = "PS Store USA 70$", Price = 6467, OldPrice = 6807, Discount = 5, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 15, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "75 USD", Amount = "75 USD", TotalDisplay = "PS Store USA 75$", Price = 6928, OldPrice = 7292, Discount = 5, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 16, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "100 USD", Amount = "100 USD", TotalDisplay = "PS Store USA 100$", Price = 9238, OldPrice = 9700, Discount = 5, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 17, IsActive = true, StockQuantity = 99 },
                new GameProduct { GiftCardId = psnCard.Id, Name = "110 USD", Amount = "110 USD", TotalDisplay = "PS Store USA 110$", Price = 10162, OldPrice = 10670, Discount = 5, Region = "USA", ProductType = GameProductType.GiftCard, SortOrder = 18, IsActive = true, StockQuantity = 99 },
            };

            foreach (var p in psnProducts)
            {
                p.CreatedAt = DateTime.UtcNow;
            }

            context.GameProducts.AddRange(psnProducts);
            await context.SaveChangesAsync();
        }
    }
}
