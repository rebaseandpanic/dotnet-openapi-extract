using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Provides a hardcoded registry that maps well-known BCL JSON container type full names
/// to <see cref="BclJsonSchemaTemplate"/> values describing the inline OpenAPI schema shape
/// to emit for those types.
/// </summary>
/// <remarks>
/// Types in this registry are emitted as <b>inline</b> schemas — they are never registered
/// in <c>components/schemas</c>. This prevents opaque named schemas from polluting the
/// components section for types like <c>JsonElement</c>, <c>JObject</c>, or
/// <c>ExpandoObject</c> that represent arbitrary JSON values.
/// </remarks>
public static class BclJsonTypeRegistry
{
    // -------------------------------------------------------------------------
    // Shape enum
    // -------------------------------------------------------------------------

    /// <summary>
    /// Describes the OpenAPI shape to emit for a BCL JSON container type.
    /// </summary>
    public enum BclJsonShape
    {
        /// <summary>
        /// Truly-any JSON value — emitted as <c>{}</c> (no type, no properties, no items).
        /// Accepts any JSON value including null, strings, numbers, booleans, objects, and arrays.
        /// </summary>
        Any,

        /// <summary>
        /// An arbitrary JSON object — emitted as <c>{ type: object, additionalProperties: {} }</c>.
        /// </summary>
        Object,

        /// <summary>
        /// An arbitrary JSON array — emitted as <c>{ type: array, items: {} }</c>.
        /// </summary>
        Array,
    }

    // -------------------------------------------------------------------------
    // Template record
    // -------------------------------------------------------------------------

    /// <summary>
    /// Describes how a BCL JSON container type should be represented in the OpenAPI schema.
    /// </summary>
    public sealed record BclJsonSchemaTemplate
    {
        /// <summary>
        /// The shape of the emitted inline schema.
        /// </summary>
        public required BclJsonShape Shape { get; init; }

        /// <summary>
        /// The default description text applied to the schema when no property-level
        /// <c>[Description]</c> attribute is present.
        /// </summary>
        public required string DefaultDescription { get; init; }
    }

    // -------------------------------------------------------------------------
    // Registry lookup table
    // -------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, BclJsonSchemaTemplate> Registry =
        new Dictionary<string, BclJsonSchemaTemplate>(StringComparer.Ordinal)
        {
            // System.Text.Json — any-value types
            ["System.Text.Json.JsonElement"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Any,
                DefaultDescription = "Arbitrary JSON value",
            },
            ["System.Text.Json.Nodes.JsonNode"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Any,
                DefaultDescription = "Arbitrary JSON value",
            },
            ["System.Text.Json.Nodes.JsonValue"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Any,
                DefaultDescription = "Arbitrary JSON value",
            },
            ["System.Text.Json.JsonDocument"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Any,
                DefaultDescription = "Arbitrary JSON value",
            },

            // Newtonsoft.Json — any-value types
            ["Newtonsoft.Json.Linq.JToken"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Any,
                DefaultDescription = "Arbitrary JSON value",
            },
            ["Newtonsoft.Json.Linq.JValue"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Any,
                DefaultDescription = "Arbitrary JSON value",
            },
            ["Newtonsoft.Json.Linq.JRaw"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Any,
                DefaultDescription = "Arbitrary JSON value",
            },

            // System.Text.Json — object types
            ["System.Text.Json.Nodes.JsonObject"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Object,
                DefaultDescription = "Arbitrary JSON object",
            },

            // Newtonsoft.Json — object types
            ["Newtonsoft.Json.Linq.JObject"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Object,
                DefaultDescription = "Arbitrary JSON object",
            },

            // System.Dynamic — object types
            ["System.Dynamic.ExpandoObject"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Object,
                DefaultDescription = "Arbitrary JSON object",
            },

            // System.Text.Json — array types
            ["System.Text.Json.Nodes.JsonArray"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Array,
                DefaultDescription = "Arbitrary JSON array",
            },

            // Newtonsoft.Json — array types
            ["Newtonsoft.Json.Linq.JArray"] = new BclJsonSchemaTemplate
            {
                Shape = BclJsonShape.Array,
                DefaultDescription = "Arbitrary JSON array",
            },
        };

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a <see cref="BclJsonSchemaTemplate"/> for the given type full name,
    /// or <see langword="null"/> if the type is not in the registry.
    /// </summary>
    /// <param name="typeFullName">
    /// The <see cref="Type.FullName"/> of the BCL type as returned by MetadataLoadContext reflection.
    /// </param>
    public static BclJsonSchemaTemplate? TryGet(string typeFullName)
    {
        if (string.IsNullOrEmpty(typeFullName))
            return null;

        Registry.TryGetValue(typeFullName, out var template);
        return template;
    }

    /// <summary>
    /// Creates an <see cref="OpenApiSchema"/> for the given <see cref="BclJsonSchemaTemplate"/>.
    /// The schema is always inline (never a <c>$ref</c>) and carries a default description
    /// that can be overridden by a property-level <c>[Description]</c> attribute.
    /// </summary>
    /// <param name="template">The template describing the shape to emit.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><see cref="BclJsonShape.Any"/> → <c>{}</c> (no type, no properties, no items)</item>
    ///   <item><see cref="BclJsonShape.Object"/> → <c>{ type: object, additionalProperties: {} }</c></item>
    ///   <item><see cref="BclJsonShape.Array"/> → <c>{ type: array, items: {} }</c></item>
    /// </list>
    /// </returns>
    public static OpenApiSchema CreateSchema(BclJsonSchemaTemplate template)
    {
        return template.Shape switch
        {
            BclJsonShape.Object => new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                AdditionalProperties = new OpenApiSchema(),
                Description = template.DefaultDescription,
            },
            BclJsonShape.Array => new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema(),
                Description = template.DefaultDescription,
            },
            _ => new OpenApiSchema
            {
                Description = template.DefaultDescription,
            },
        };
    }
}
