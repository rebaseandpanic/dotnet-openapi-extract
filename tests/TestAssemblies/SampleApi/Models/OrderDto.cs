namespace SampleApi.Models;

/// <summary>
/// Order data
/// </summary>
public class OrderDto
{
    /// <summary>Order ID</summary>
    public Guid Id { get; set; }
    /// <summary>Order total amount</summary>
    public decimal TotalAmount { get; set; }
    /// <summary>Order status</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Order creation date</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Order item data
/// </summary>
public class OrderItemDto
{
    /// <summary>Item ID</summary>
    public int Id { get; set; }
    /// <summary>Product name</summary>
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Quantity</summary>
    public int Quantity { get; set; }
    /// <summary>Unit price</summary>
    public decimal UnitPrice { get; set; }
}
