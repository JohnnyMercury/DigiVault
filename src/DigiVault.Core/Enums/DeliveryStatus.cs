namespace DigiVault.Core.Enums;

/// <summary>
/// Per-OrderItem delivery state. Independent from Order.Status — an order can
/// be Processing while individual items move through Pending → Delivered.
/// </summary>
public enum DeliveryStatus
{
    /// <summary>Awaiting fulfilment.</summary>
    Pending = 1,
    /// <summary>Credential generated and stored in DeliveryPayloadJson.</summary>
    Delivered = 2,
    /// <summary>Generation failed; needs admin intervention.</summary>
    Failed = 3
}
