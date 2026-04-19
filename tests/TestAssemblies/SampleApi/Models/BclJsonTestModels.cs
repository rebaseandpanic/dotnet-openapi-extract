using System.ComponentModel;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;

namespace SampleApi.Models;

/// <summary>
/// DTO covering every BCL JSON container type in BclJsonTypeRegistry,
/// plus edge cases (nullable, arrays, dictionaries, [Description] override).
/// </summary>
public class BclJsonDto
{
    /// <summary>Any JSON value via JsonElement</summary>
    public JsonElement Element { get; set; }

    /// <summary>Nullable JsonNode reference type</summary>
    public JsonNode? Node { get; set; }

    /// <summary>Nullable JsonDocument reference type</summary>
    public JsonDocument? Document { get; set; }

    /// <summary>Nullable JsonObject — arbitrary JSON object</summary>
    public JsonObject? Object { get; set; }

    /// <summary>Nullable JsonArray — arbitrary JSON array</summary>
    public JsonArray? Array { get; set; }

    /// <summary>Nullable JsonValue — arbitrary JSON value</summary>
    public JsonValue? Value { get; set; }

    /// <summary>Nullable Newtonsoft JToken</summary>
    public JToken? Token { get; set; }

    /// <summary>Nullable Newtonsoft JObject</summary>
    public JObject? JObject { get; set; }

    /// <summary>Nullable Newtonsoft JArray</summary>
    public JArray? JArray { get; set; }

    /// <summary>Nullable Newtonsoft JValue</summary>
    public JValue? JValue { get; set; }

    /// <summary>Nullable Newtonsoft JRaw</summary>
    public JRaw? JRaw { get; set; }

    /// <summary>Nullable ExpandoObject — arbitrary JSON object</summary>
    public ExpandoObject? Expando { get; set; }

    /// <summary>Dictionary with JsonElement values — additionalProperties should be truly-any</summary>
    public Dictionary<string, JsonElement>? DictionaryOfJsonElement { get; set; }

    /// <summary>Dictionary with string values — unchanged, additionalProperties is string schema</summary>
    public Dictionary<string, string>? DictionaryOfString { get; set; }

    /// <summary>Dictionary with typed DTO values — additionalProperties should be $ref</summary>
    public Dictionary<string, TypedDto>? DictionaryOfTypedDto { get; set; }

    /// <summary>Array of JsonElement — items should be truly-any</summary>
    public JsonElement[]? ArrayOfJsonElement { get; set; }

    /// <summary>List of JsonElement — items should be truly-any</summary>
    public List<JsonElement>? ListOfJsonElement { get; set; }

    /// <summary>Nullable struct Nullable&lt;JsonElement&gt;</summary>
    public JsonElement? NullableJsonElement { get; set; }

    /// <summary>JsonElement with property-level [Description] — user description must win</summary>
    [Description("Request metadata as free-form JSON")]
    public JsonElement MetadataWithDescription { get; set; }

    /// <summary>Nullable JsonNode with property-level [Description] — tests Description + MakeNullable composition</summary>
    [Description("Tenant-specific config blob")]
    public JsonNode? TenantConfig { get; set; }
}

/// <summary>
/// A typed DTO used as the value type in a negative-case dictionary test.
/// Verifies that Dictionary&lt;string, TypedDto&gt; still produces a $ref for the value schema,
/// not an inline BCL schema.
/// </summary>
public class TypedDto
{
    /// <summary>The identifier</summary>
    public int Id { get; set; }

    /// <summary>The label</summary>
    public string? Label { get; set; }
}

/// <summary>
/// Minimal request body DTO used in integration tests.
/// </summary>
public class JsonBlobRequest
{
    /// <summary>Raw JSON payload</summary>
    public JsonElement Payload { get; set; }
}
