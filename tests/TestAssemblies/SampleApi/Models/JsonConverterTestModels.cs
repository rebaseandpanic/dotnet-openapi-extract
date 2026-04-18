using System.Text.Json.Serialization;

namespace SampleApi.Models;

/// <summary>
/// DTO used for testing property-level [JsonConverter] handling in schema generation.
/// </summary>
public class JsonConverterTestDto
{
    /// <summary>Enum serialized as string via JsonStringEnumConverter on the property.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConverterTestStatus State { get; set; }

    /// <summary>Plain DateTime without any converter — should stay as string/date-time from PrimitiveMap.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>String field without special converter.</summary>
    public string? Label { get; set; }
}

/// <summary>
/// Enum used by JsonConverterTestDto — no type-level [JsonConverter] on this enum.
/// This tests the property-level converter path.
/// </summary>
public enum ConverterTestStatus
{
    /// <summary>Active status.</summary>
    Active,
    /// <summary>Inactive status.</summary>
    Inactive,
    /// <summary>Pending status.</summary>
    Pending,
}
