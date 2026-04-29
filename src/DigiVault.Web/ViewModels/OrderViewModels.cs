using DigiVault.Core.Enums;
using DigiVault.Web.Services.Fulfilment;

namespace DigiVault.Web.ViewModels;

public class OrderViewModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusName => Status switch
    {
        OrderStatus.Pending => "Pending",
        OrderStatus.Processing => "Processing",
        OrderStatus.Completed => "Completed",
        OrderStatus.Cancelled => "Cancelled",
        OrderStatus.Refunded => "Refunded",
        _ => "Unknown"
    };
    public string StatusClass => Status switch
    {
        OrderStatus.Pending => "warning",
        OrderStatus.Processing => "info",
        OrderStatus.Completed => "success",
        OrderStatus.Cancelled => "secondary",
        OrderStatus.Refunded => "danger",
        _ => "secondary"
    };
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<OrderItemViewModel> Items { get; set; } = new();
}

public class OrderItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }

    /// <summary>Per-item delivery state (Pending / Delivered / Failed).</summary>
    public DeliveryStatus DeliveryStatus { get; set; }

    /// <summary>Decoded payload (CodeCredential or ConfirmationCredential) — null while delivery is Pending.</summary>
    public DeliveryPayload? Delivery { get; set; }

    /// <summary>Legacy support — old orders that still use ProductKey rows.</summary>
    public List<string> ProductKeys { get; set; } = new();
}

public class OrderHistoryViewModel
{
    public List<OrderViewModel> Orders { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
}

public class TransactionViewModel
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string TypeName => Type switch
    {
        TransactionType.Deposit => "Deposit",
        TransactionType.Purchase => "Purchase",
        TransactionType.Refund => "Refund",
        _ => "Unknown"
    };
    public string TypeClass => Type switch
    {
        TransactionType.Deposit => "success",
        TransactionType.Purchase => "danger",
        TransactionType.Refund => "info",
        _ => "secondary"
    };
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
