using DigiVault.Core.Entities;

namespace DigiVault.Web.Models;

/// <summary>
/// Shared view model for the _ProductReviews partial rendered on each product page.
/// </summary>
public class ProductReviewsViewModel
{
    public IReadOnlyList<ProductReview> Reviews { get; set; } = Array.Empty<ProductReview>();
    public int TotalCount { get; set; }
    public double AverageRating { get; set; }

    /// <summary>"Game" | "GiftCard" | "VpnProvider" — used by the review submission form.</summary>
    public string ProductType { get; set; } = "";
    public string ProductSlug { get; set; } = "";

    public bool IsAuthenticated { get; set; }
    /// <summary>True if the current user has a completed order containing this specific product.</summary>
    public bool CanReview { get; set; }
}
