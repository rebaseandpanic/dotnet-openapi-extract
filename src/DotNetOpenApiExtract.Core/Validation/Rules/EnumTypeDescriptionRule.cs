using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>enum.type-description</c>
/// Enum schemas (top-level schemas with non-empty <c>enum[]</c>) must have a non-empty description
/// that mentions every enum value by name.
/// <para>
/// "Mentions" is a case-sensitive substring check of each value's string representation
/// against the schema description.
/// </para>
/// </summary>
public sealed class EnumTypeDescriptionRule : IValidationRule
{
    public string Id => "enum.type-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;
            if (s.Enum == null || s.Enum.Count == 0) continue;

            if (string.IsNullOrWhiteSpace(s.Description))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    resolver.ForSchema(schemaId),
                    $"Enum schema '{schemaId}' is missing a description.");
                continue;
            }

            // Check that all enum values are mentioned in the description
            var missingValues = new List<string>();
            foreach (var enumValue in s.Enum)
            {
                var valueStr = GetStringValue(enumValue);
                if (string.IsNullOrEmpty(valueStr)) continue;

                if (!s.Description.Contains(valueStr, StringComparison.Ordinal))
                    missingValues.Add(valueStr);
            }

            if (missingValues.Count > 0)
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    resolver.ForSchema(schemaId),
                    $"Enum schema '{schemaId}' description does not mention all values. Missing: {string.Join(", ", missingValues)}.");
            }
        }
    }

    /// <summary>
    /// Extracts a string representation from a JsonNode enum value.
    /// String JsonValues (from string enums) return their actual string content.
    /// Numeric JsonValues return their numeric string.
    /// </summary>
    private static string? GetStringValue(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s)) return s;
            return jv.ToString();
        }
        return node.ToString();
    }
}
