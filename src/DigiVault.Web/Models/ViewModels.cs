using DigiVault.Core.Entities;
using DigiVault.Core.Enums;

namespace DigiVault.Web.Models;

public class LoginViewModel
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public class RegisterViewModel
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class HomeViewModel
{
    public IEnumerable<CatalogItemViewModel>? FeaturedItems { get; set; }
    public int TotalProducts { get; set; }
    public int TotalUsers { get; set; }
}

/// <summary>
/// Unified catalog item representing a parent product (Game, GiftCard, or VpnProvider)
/// </summary>
public class CatalogItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Icon { get; set; } = "🎮";
    public string Gradient { get; set; } = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)";
    public string Category { get; set; } = string.Empty; // "games", "giftcards", "vpn"
    public string CategoryDisplay { get; set; } = string.Empty; // "Игровая валюта", "Подарочная карта", "VPN"
    public string DetailUrl { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxDiscount { get; set; }
}

public class CatalogViewModel
{
    public IEnumerable<CatalogItemViewModel>? Items { get; set; }
    public string? CategoryFilter { get; set; }
    public string? SearchQuery { get; set; }
    public string? SortBy { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
}

public class DashboardViewModel
{
    public ApplicationUser? User { get; set; }
    public IEnumerable<Order>? RecentOrders { get; set; }
    public int TotalOrders { get; set; }
    public int CompletedOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public int TotalKeys { get; set; }
}

public class OrdersViewModel
{
    public IEnumerable<Order>? Orders { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
}

public class OrderDetailsViewModel
{
    public Order? Order { get; set; }
    public IEnumerable<ProductKey>? Keys { get; set; }
}

public class DepositViewModel
{
    public decimal Amount { get; set; }
    public decimal CurrentBalance { get; set; }
    public string PaymentMethod { get; set; } = "card";
}
