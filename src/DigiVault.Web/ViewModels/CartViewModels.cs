namespace DigiVault.Web.ViewModels;

public class CartViewModel
{
    public List<CartItemViewModel> Items { get; set; } = new();
    public decimal Subtotal => Items.Sum(x => x.TotalPrice);
    public decimal Total => Subtotal;
    public int TotalItems => Items.Sum(x => x.Quantity);
    public bool IsEmpty => Items.Count == 0;
}

public class CartItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal TotalPrice => UnitPrice * Quantity;
    public int MaxQuantity { get; set; }
}

public class AddToCartViewModel
{
    public int ProductId { get; set; }
    public int Quantity { get; set; } = 1;
}

public class UpdateCartItemViewModel
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
