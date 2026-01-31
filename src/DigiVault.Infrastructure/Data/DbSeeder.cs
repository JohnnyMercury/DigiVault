using DigiVault.Core.Entities;
using DigiVault.Core.Enums;
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

        // Seed products if empty
        if (!await context.Products.AnyAsync())
        {
            var products = new List<Product>
            {
                // VPN Subscriptions
                new()
                {
                    Name = "NordVPN - 1 Month",
                    Description = "Premium VPN service with 5500+ servers in 60 countries. Protect your online privacy.",
                    Price = 12.99m,
                    Category = ProductCategory.VpnSubscription,
                    ImageUrl = "/images/products/nordvpn.png",
                    StockQuantity = 100,
                    Metadata = "{\"duration\": 30, \"provider\": \"NordVPN\"}"
                },
                new()
                {
                    Name = "NordVPN - 1 Year",
                    Description = "Premium VPN service with 5500+ servers in 60 countries. Best value!",
                    Price = 59.99m,
                    OldPrice = 155.88m,
                    Category = ProductCategory.VpnSubscription,
                    ImageUrl = "/images/products/nordvpn.png",
                    StockQuantity = 100,
                    Metadata = "{\"duration\": 365, \"provider\": \"NordVPN\"}"
                },
                new()
                {
                    Name = "ExpressVPN - 1 Month",
                    Description = "Ultra-fast VPN with servers in 94 countries. Stream without limits.",
                    Price = 14.99m,
                    Category = ProductCategory.VpnSubscription,
                    ImageUrl = "/images/products/expressvpn.png",
                    StockQuantity = 50,
                    Metadata = "{\"duration\": 30, \"provider\": \"ExpressVPN\"}"
                },
                new()
                {
                    Name = "ExpressVPN - 1 Year",
                    Description = "Ultra-fast VPN with servers in 94 countries. Save 49%!",
                    Price = 89.99m,
                    OldPrice = 179.88m,
                    Category = ProductCategory.VpnSubscription,
                    ImageUrl = "/images/products/expressvpn.png",
                    StockQuantity = 50,
                    Metadata = "{\"duration\": 365, \"provider\": \"ExpressVPN\"}"
                },

                // Game Currencies
                new()
                {
                    Name = "Fortnite - 1000 V-Bucks",
                    Description = "In-game currency for Fortnite. Buy skins, emotes, and battle passes!",
                    Price = 7.99m,
                    Category = ProductCategory.GameCurrency,
                    ImageUrl = "/images/products/vbucks.png",
                    StockQuantity = 200,
                    Metadata = "{\"game\": \"Fortnite\", \"amount\": 1000, \"currency\": \"V-Bucks\"}"
                },
                new()
                {
                    Name = "Fortnite - 2800 V-Bucks",
                    Description = "In-game currency for Fortnite. Best value pack!",
                    Price = 19.99m,
                    OldPrice = 22.37m,
                    Category = ProductCategory.GameCurrency,
                    ImageUrl = "/images/products/vbucks.png",
                    StockQuantity = 150,
                    Metadata = "{\"game\": \"Fortnite\", \"amount\": 2800, \"currency\": \"V-Bucks\"}"
                },
                new()
                {
                    Name = "Roblox - 800 Robux",
                    Description = "Official Robux for Roblox. Customize your avatar and unlock premium content!",
                    Price = 9.99m,
                    Category = ProductCategory.GameCurrency,
                    ImageUrl = "/images/products/robux.png",
                    StockQuantity = 300,
                    Metadata = "{\"game\": \"Roblox\", \"amount\": 800, \"currency\": \"Robux\"}"
                },
                new()
                {
                    Name = "Roblox - 2200 Robux",
                    Description = "Official Robux for Roblox. Premium pack with bonus!",
                    Price = 24.99m,
                    OldPrice = 27.47m,
                    Category = ProductCategory.GameCurrency,
                    ImageUrl = "/images/products/robux.png",
                    StockQuantity = 200,
                    Metadata = "{\"game\": \"Roblox\", \"amount\": 2200, \"currency\": \"Robux\"}"
                },

                // Gift Cards
                new()
                {
                    Name = "Steam Gift Card - $10",
                    Description = "Digital Steam Wallet code. Instant delivery!",
                    Price = 10.49m,
                    Category = ProductCategory.GiftCard,
                    ImageUrl = "/images/products/steam.png",
                    StockQuantity = 100,
                    Metadata = "{\"platform\": \"Steam\", \"value\": 10, \"currency\": \"USD\"}"
                },
                new()
                {
                    Name = "Steam Gift Card - $50",
                    Description = "Digital Steam Wallet code. Best for gamers!",
                    Price = 51.99m,
                    Category = ProductCategory.GiftCard,
                    ImageUrl = "/images/products/steam.png",
                    StockQuantity = 80,
                    Metadata = "{\"platform\": \"Steam\", \"value\": 50, \"currency\": \"USD\"}"
                },
                new()
                {
                    Name = "PlayStation Store - $25",
                    Description = "PSN Gift Card for PlayStation Store purchases.",
                    Price = 25.99m,
                    Category = ProductCategory.GiftCard,
                    ImageUrl = "/images/products/psn.png",
                    StockQuantity = 75,
                    Metadata = "{\"platform\": \"PlayStation\", \"value\": 25, \"currency\": \"USD\"}"
                },
                new()
                {
                    Name = "Xbox Gift Card - $25",
                    Description = "Microsoft Xbox Gift Card for games and subscriptions.",
                    Price = 25.99m,
                    Category = ProductCategory.GiftCard,
                    ImageUrl = "/images/products/xbox.png",
                    StockQuantity = 75,
                    Metadata = "{\"platform\": \"Xbox\", \"value\": 25, \"currency\": \"USD\"}"
                },
                new()
                {
                    Name = "Netflix Gift Card - $30",
                    Description = "Netflix prepaid gift card. Stream movies and TV shows!",
                    Price = 30.99m,
                    Category = ProductCategory.GiftCard,
                    ImageUrl = "/images/products/netflix.png",
                    StockQuantity = 60,
                    Metadata = "{\"platform\": \"Netflix\", \"value\": 30, \"currency\": \"USD\"}"
                },
                new()
                {
                    Name = "Spotify Premium - 3 Months",
                    Description = "Spotify Premium subscription code. Ad-free music!",
                    Price = 29.99m,
                    Category = ProductCategory.GiftCard,
                    ImageUrl = "/images/products/spotify.png",
                    StockQuantity = 50,
                    Metadata = "{\"platform\": \"Spotify\", \"duration\": 90, \"type\": \"Premium\"}"
                }
            };

            await context.Products.AddRangeAsync(products);
            await context.SaveChangesAsync();

            // Add sample product keys
            foreach (var product in products)
            {
                var keys = new List<ProductKey>();
                for (int i = 0; i < Math.Min(product.StockQuantity, 10); i++)
                {
                    keys.Add(new ProductKey
                    {
                        ProductId = product.Id,
                        KeyValue = $"DEMO-{product.Id:D4}-{Guid.NewGuid().ToString()[..8].ToUpper()}"
                    });
                }
                await context.ProductKeys.AddRangeAsync(keys);
            }
            await context.SaveChangesAsync();
        }
    }
}
