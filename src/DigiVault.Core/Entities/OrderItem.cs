using DigiVault.Core.Enums;

namespace DigiVault.Core.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int GameProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

    /// <summary>
    /// Per-item delivery state. Set to Pending on order creation, flipped to
    /// Delivered by IFulfilmentService once <see cref="DeliveryPayloadJson"/>
    /// has been generated and persisted.
    /// </summary>
    public DeliveryStatus DeliveryStatus { get; set; } = DeliveryStatus.Pending;

    /// <summary>
    /// JSON payload describing the actual delivered credential. Schema lives in
    /// <c>DigiVault.Web/Services/Fulfilment/DeliveryPayload.cs</c>.
    /// Stored as <c>jsonb</c> in PostgreSQL.
    /// </summary>
    public string? DeliveryPayloadJson { get; set; }

    /// <summary>UTC timestamp when delivery was completed.</summary>
    public DateTime? DeliveredAt { get; set; }

    public virtual Order Order { get; set; } = null!;
    public virtual GameProduct GameProduct { get; set; } = null!;
    public virtual ICollection<ProductKey> ProductKeys { get; set; } = new List<ProductKey>();
}
