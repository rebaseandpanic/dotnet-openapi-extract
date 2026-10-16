using System.Text.Json.Nodes;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.no-duplicate-enum</c>
/// Enum arrays must not contain duplicate values. Duplicate enum values confuse code generators
/// and produce duplicate case labels in generated code.
/// Spectral <c>duplicated-entry-in-enum</c> warn.
/// <para>
/// A property that is a reference is read by its own keywords (<see cref="ReferencePlace.OwnSiblings"/>):
/// the 3.0 <c>allOf</c> wrapper and the siblings of <c>$ref</c> in 3.1+ alike; the referenced component
/// is not read, and a bare reference is skipped.
/// </para>
/// </summary>
public sealed class SchemaNoDuplicateEnumRule : IValidationRule
{
    public string Id => "schema.no-duplicate-enum";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;

            foreach (var v in CheckSchema(s, JsonPointerHelper.ForSchema(schemaId)))
                yield return v;

            if (s.Properties != null)
            {
                foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key))
                {
                    if (ReferencePlace.OwnSiblings(propSchema) is not { } ps) continue;
                    foreach (var v in CheckSchema(ps, JsonPointerHelper.ForSchemaProperty(schemaId, propName)))
                        yield return v;
                }
            }
        }
    }

    private IEnumerable<ValidationViolation> CheckSchema(OpenApiSchema schema, string pointer)
    {
        if (schema.Enum == null || schema.Enum.Count <= 1) yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        foreach (var enumVal in schema.Enum)
        {
            var str = enumVal?.ToString() ?? "null";
            if (!seen.Add(str))
                duplicates.Add(str);
        }

        foreach (var dup in duplicates.Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal))
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                $"{pointer}/enum",
                null,
                $"Enum at '{pointer}' contains duplicate value '{dup}'.");
        }
    }
}
