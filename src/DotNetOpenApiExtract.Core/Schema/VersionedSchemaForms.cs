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
}
