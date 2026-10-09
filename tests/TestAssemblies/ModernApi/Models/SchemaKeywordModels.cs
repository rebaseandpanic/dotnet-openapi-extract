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
