namespace SampleApi.Models;

/// <summary>
/// Product data
/// </summary>
public class ProductDto
{
    /// <summary>Product ID</summary>
    public int Id { get; set; }
    /// <summary>Product name</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Price</summary>
    public decimal Price { get; set; }
    /// <summary>Is available</summary>
    public bool InStock { get; set; }
}
