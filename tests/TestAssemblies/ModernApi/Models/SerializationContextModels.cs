namespace ModernApi.Models.Contexts;

/// <summary>Order summary whose multi-word property names differ under every naming policy.</summary>
public class OrderSummary
{
    /// <summary>Human-readable order number.</summary>
    public required string OrderNumber { get; set; }

    /// <summary>Number of items in the order.</summary>
    public required int ItemCount { get; set; }

    /// <summary>Optional note left by the customer.</summary>
    public string? CustomerNote { get; set; }
}
