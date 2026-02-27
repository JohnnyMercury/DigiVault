using DigiVault.Core.Enums;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DigiVault.Web.ViewModels;

public class ProductCreateViewModel
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(200, ErrorMessage = "Name cannot exceed 200 characters")]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Price is required")]
    [Range(0.01, 1000000, ErrorMessage = "Price must be between 0.01 and 1,000,000")]
    public decimal Price { get; set; }

    [Range(0, 1000000, ErrorMessage = "Old price must be between 0 and 1,000,000")]
    public decimal? OldPrice { get; set; }

    [Required(ErrorMessage = "Category is required")]
    public ProductCategory Category { get; set; }

    public IFormFile? ImageFile { get; set; }

    public string? ImageUrl { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Stock quantity cannot be negative")]
    public int StockQuantity { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsFeatured { get; set; } = false;

    public string? Metadata { get; set; }
}

public class ProductEditViewModel : ProductCreateViewModel
{
    public int Id { get; set; }

    public string? CurrentImageUrl { get; set; }
}
