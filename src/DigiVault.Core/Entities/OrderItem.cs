namespace DigiVault.Core.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int GameProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

    public virtual Order Order { get; set; } = null!;
    public virtual GameProduct GameProduct { get; set; } = null!;
    public virtual ICollection<ProductKey> ProductKeys { get; set; } = new List<ProductKey>();
}
