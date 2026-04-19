using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.enum-filled</c>
/// If a property's schema has an enum type declaration, the <c>enum</c> array must be non-empty.
/// </summary>
public sealed class SchemaEnumFilledRule : IValidationRule
{
    public string Id => "schema.enum-filled";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;

            // Check top-level enum schema itself
            if (s.Enum != null && s.Enum.Count == 0)
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    resolver.ForSchema(schemaId),
                    $"Schema '{schemaId}' has an empty enum array.");
            }

            // Check properties
            if (s.Properties == null) continue;
            foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key))
            {
                if (propSchema is not OpenApiSchema prop) continue;

                // Enum on property schema (inline enum)
                if (prop.Enum != null && prop.Enum.Count == 0)
                {
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                        resolver.ForSchemaProperty(schemaId, propName),
                        $"Property '{propName}' in schema '{schemaId}' has an empty enum array.");
                }
            }
        }
    }
}
