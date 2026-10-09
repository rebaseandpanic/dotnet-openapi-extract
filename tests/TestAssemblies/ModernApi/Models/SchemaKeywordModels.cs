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

/// <summary>Extension data as <c>IDictionary&lt;string, object&gt;</c>.</summary>
public class ExtensionObjectBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, object>? Extra { get; set; }
}

/// <summary>Extension data as <c>IDictionary&lt;string, JsonElement&gt;</c>.</summary>
public class ExtensionElementBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>Extension data as a concrete dictionary.</summary>
public class ExtensionDictionaryBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, object>? Extra { get; set; }
}

/// <summary>Extension data as <c>JsonObject</c>.</summary>
public class ExtensionJsonObjectBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public System.Text.Json.Nodes.JsonObject? Extra { get; set; }
}

/// <summary>Inherits its extension data.</summary>
public class ExtensionDerivedBag : ExtensionDictionaryBag
{
    /// <summary>Declared level.</summary>
    public int Level { get; set; }
}

/// <summary>Extension data that is ignored, so the object is not open.</summary>
public class ExtensionIgnoredBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Ignored member.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, object>? Extra { get; set; }
}

/// <summary>A generic type with extension data.</summary>
public class ExtensionEnvelope<T>
{
    /// <summary>The payload.</summary>
    public required T Payload { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>Numeric ranges in every declared form.</summary>
public class RangeModel
{
    /// <summary>Inclusive integer range.</summary>
    [Range(1, 10)]
    public int Inclusive { get; set; }

    /// <summary>Integer range with an exclusive minimum.</summary>
    [Range(1, 10, MinimumIsExclusive = true)]
    public int ExclusiveMin { get; set; }

    /// <summary>Double range with an exclusive maximum.</summary>
    [Range(0.5, 9.5, MaximumIsExclusive = true)]
    public double ExclusiveMax { get; set; }

    /// <summary>Decimal range, both sides exclusive.</summary>
    [Range(typeof(decimal), "0.01", "999.99", MinimumIsExclusive = true, MaximumIsExclusive = true)]
    public decimal ExclusiveBoth { get; set; }

    /// <summary>Decimal range from strings.</summary>
    [Range(typeof(decimal), "0.01", "999.99")]
    public decimal Price { get; set; }

    /// <summary>Decimal range with 28 significant digits.</summary>
    [Range(typeof(decimal), "0.1234567890123456789012345678", "1")]
    public decimal Precise { get; set; }

    /// <summary>Nullable integer range.</summary>
    [Range(-5, 5)]
    public int? Optional { get; set; }

    /// <summary>A range over dates, which are not JSON numbers.</summary>
    [Range(typeof(DateTime), "2020-01-01", "2030-01-01")]
    public DateTime When { get; set; }

    /// <summary>A range with an infinite minimum.</summary>
    [Range(double.NegativeInfinity, 5.0)]
    public double Unbounded { get; set; }

    /// <summary>A range with a NaN minimum, which RangeAttribute accepts.</summary>
    [Range(typeof(double), "NaN", "5")]
    public double NotANumberMin { get; set; }
}
