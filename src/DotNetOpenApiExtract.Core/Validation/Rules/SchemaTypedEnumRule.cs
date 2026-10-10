using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.typed-enum</c>
/// When a schema has both a declared <c>type</c> and an <c>enum</c> array, each enum value must
/// be compatible with the declared type. Type-mismatched enums break client deserialization.
/// Spectral <c>typed-enum</c> recommended. Redocly <c>no-enum-type-mismatch</c> error.
/// </summary>
public sealed class SchemaTypedEnumRule : IValidationRule
{
    public string Id => "schema.typed-enum";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;

            foreach (var v in CheckSchema(s, JsonPointerHelper.ForSchema(schemaId), schemaId))
                yield return v;

            if (s.Properties != null)
            {
                foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key))
                {
                    if (propSchema is not OpenApiSchema ps) continue;
                    foreach (var v in CheckSchema(ps,
                        JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                        $"{schemaId}.{propName}"))
                        yield return v;
                }
            }
        }
    }

    private IEnumerable<ValidationViolation> CheckSchema(OpenApiSchema schema, string pointer, string name)
    {
        if (schema.Enum == null || schema.Enum.Count == 0) yield break;
        if (schema.Type == null) yield break; // no declared type — nothing to check against

        var expectedKind = GetExpectedNodeKind(schema.Type.Value);
        if (expectedKind == null) yield break; // object/array/null — skip

        for (int i = 0; i < schema.Enum.Count; i++)
        {
            var enumVal = schema.Enum[i];
            if (enumVal == null) continue;

            if (!IsCompatible(enumVal, schema.Type.Value))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    $"{pointer}/enum/{i}",
                    null,
                    $"Enum value '{enumVal}' in '{name}' is not compatible with declared type '{schema.Type}'.");
            }
        }
    }

    /// <summary>
    /// Whether the JSON value <paramref name="node"/> is of <paramref name="type"/>, judged by its
    /// JSON kind and, for an integer, by its number text — never by the CLR type that holds it,
    /// which depends on the reader (a JSON file, a YAML file, a document built in memory).
    /// </summary>
    private static bool IsCompatible(JsonNode node, JsonSchemaType type)
    {
        if (node is not JsonValue jv) return false; // complex node

        var kind = jv.GetValueKind();
        return type switch
        {
            JsonSchemaType.Integer => kind == JsonValueKind.Number && IsIntegral(jv.ToJsonString()),
            JsonSchemaType.Number  => kind == JsonValueKind.Number,
            JsonSchemaType.String  => kind == JsonValueKind.String,
            JsonSchemaType.Boolean => kind is JsonValueKind.True or JsonValueKind.False,
            _ => true, // other types — pass
        };
    }

    /// <summary>Whether the JSON number <paramref name="text"/> has no fractional part (<c>2</c>, <c>2.0</c>, <c>2e3</c>).</summary>
    private static bool IsIntegral(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact)
            ? exact == decimal.Truncate(exact)
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var approximate)
              && double.IsFinite(approximate) && approximate == Math.Floor(approximate);

    private static string? GetExpectedNodeKind(JsonSchemaType type) => type switch
    {
        JsonSchemaType.Integer => "integer",
        JsonSchemaType.Number  => "number",
        JsonSchemaType.String  => "string",
        JsonSchemaType.Boolean => "boolean",
        _                      => null,
    };
}
