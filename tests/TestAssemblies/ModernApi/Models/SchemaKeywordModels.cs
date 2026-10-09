using System.ComponentModel.DataAnnotations;

namespace ModernApi.Models.Keywords;

/// <summary>Base64 data in every declared form.</summary>
public class Base64Payload
{
    /// <summary>Raw bytes.</summary>
    public required byte[] Data { get; set; }

    /// <summary>Optional raw bytes.</summary>
    public byte[]? OptionalData { get; set; }

    /// <summary>A base64 string.</summary>
    [Base64String]
    public required string Token { get; set; }

    /// <summary>An optional base64 string.</summary>
    [Base64String]
    public string? OptionalToken { get; set; }

    /// <summary>A plain string, for contrast.</summary>
    public required string Plain { get; set; }
}
