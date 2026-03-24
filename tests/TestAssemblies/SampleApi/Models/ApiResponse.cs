namespace SampleApi.Models;

/// <summary>
/// Standard API response wrapper
/// </summary>
/// <typeparam name="T">Response data type</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Response data (null on error)
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Error message (null on success)
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Error code for programmatic handling
    /// </summary>
    public string? ErrorCode { get; set; }
}
