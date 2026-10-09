using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Schema forms that depend on the target version, chosen while the schema is generated (never
/// rewritten afterwards).
/// </summary>
internal static class VersionedSchemaForms
{
    /// <summary>
    /// A schema that accepts exactly <paramref name="value"/> (a string or an integer), typed by
    /// the value's JSON type: <c>const</c> for 3.1+, a one-element <c>enum</c> for 3.0. An integer
    /// is always a one-element <c>enum</c>, which is equivalent: Microsoft.OpenApi models
    /// <c>const</c> as a string and would write the number as <c>"1"</c>.
    /// </summary>
    public static OpenApiSchema SingleValue(object value, OpenApiSpecVersion version)
    {
        if (value is string text)
        {
            return version == OpenApiSpecVersion.OpenApi3_0
                ? new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create(text)] }
                : new OpenApiSchema { Type = JsonSchemaType.String, Const = text };
        }

        if (value is int number)
            return new OpenApiSchema { Type = JsonSchemaType.Integer, Enum = [JsonValue.Create(number)] };

        throw new ArgumentException($"Unsupported single value of type {value.GetType()}.", nameof(value));
    }

    /// <summary>
    /// Sets the example of <paramref name="schema"/> in the form of the version: <c>example: v</c> for
    /// 3.0, <c>examples: [v]</c> for 3.1+, where <c>example</c> is deprecated. A JSON <c>null</c> is
    /// <see cref="JsonNullSentinel.JsonNull"/>.
    /// </summary>
    public static void SetExample(OpenApiSchema schema, JsonNode value, OpenApiSpecVersion version)
    {
        if (version == OpenApiSpecVersion.OpenApi3_0)
            schema.Example = value;
        else
            schema.Examples = [value];
    }

    /// <summary>
    /// A base64 string (<c>byte[]</c>, <c>[Base64String] string</c>): <c>format: byte</c> for 3.0;
    /// <c>contentEncoding: base64</c> without <c>format</c> for 3.1+, where <c>byte</c> is not in the
    /// format registry. Nothing of the other form is set, so 3.0 output carries no
    /// <c>x-jsonschema-contentEncoding</c>.
    /// </summary>
    public static OpenApiSchema Base64(OpenApiSpecVersion version) =>
        version == OpenApiSpecVersion.OpenApi3_0
            ? new OpenApiSchema { Type = JsonSchemaType.String, Format = "byte" }
            : new OpenApiSchema { Type = JsonSchemaType.String, ContentEncoding = "base64" };

    /// <summary>
    /// The nullable form of an <c>anyOf</c> union, by version: 3.0 marks the union itself
    /// <c>nullable: true</c> (the library writes a <c>null</c> type that way); 3.1+ adds a
    /// <c>{type: "null"}</c> branch, since a sibling <c>type: "null"</c> would require both.
    /// </summary>
    public static OpenApiSchema NullableUnion(OpenApiSchema union, OpenApiSpecVersion version)
    {
        if (version == OpenApiSpecVersion.OpenApi3_0)
            union.Type = JsonSchemaType.Null;
        else
            union.AnyOf!.Add(new OpenApiSchema { Type = JsonSchemaType.Null });
        return union;
    }
}
