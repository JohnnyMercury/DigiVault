using DigiVault.Core.Entities;
using DigiVault.Core.Enums;

namespace DigiVault.Web.ViewModels;

public class CatalogViewModel
{
    public List<ProductViewModel> Products { get; set; } = new();
    public ProductCategory? Category { get; set; }
    public string? SearchQuery { get; set; }
    public string? SortBy { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalProducts { get; set; }
}

public class ProductViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public ProductCategory Category { get; set; }
    public string CategoryName => Category switch
    {
        ProductCategory.VpnSubscription => "VPN",
        ProductCategory.GameCurrency => "Game Currency",
        ProductCategory.GiftCard => "Gift Card",
        _ => "Other"
    };
    public string? ImageUrl { get; set; }
    public int StockQuantity { get; set; }
    public bool InStock => StockQuantity > 0;
    public int? DiscountPercent => OldPrice.HasValue && OldPrice > Price
        ? (int)Math.Round((1 - Price / OldPrice.Value) * 100)
        : null;

    public static ProductViewModel FromEntity(Product product)
    {
        return new ProductViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price,
            OldPrice = product.OldPrice,
            Category = product.Category,
            ImageUrl = product.ImageUrl,
            StockQuantity = product.StockQuantity
        };
    }
}

public class ProductDetailsViewModel : ProductViewModel
{
    public string? Metadata { get; set; }
    public List<ProductViewModel> RelatedProducts { get; set; } = new();
}
