namespace ModernApi.Models;

/// <summary>Search filter sent as a request body.</summary>
public class SearchFilter
{
    /// <summary>Text to search for.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Maximum number of results.</summary>
    public int Limit { get; set; }
}
