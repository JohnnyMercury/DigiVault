namespace DigiVault.Core.Entities;

public class ProductKey
{
    public int Id { get; set; }
    public int GameProductId { get; set; }
    public string KeyValue { get; set; } = string.Empty;
    public bool IsUsed { get; set; } = false;
    public int? OrderItemId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }

    public virtual GameProduct GameProduct { get; set; } = null!;
    public virtual OrderItem? OrderItem { get; set; }
}
