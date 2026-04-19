using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.additional-properties-explicit</c> (off by default, Warning severity)
/// For each named schema in <c>components/schemas</c> that is an object schema with properties
/// and is not a composition schema (no allOf/anyOf/oneOf), the <c>additionalProperties</c> field
/// must be explicitly set (not null/absent).
/// </summary>
/// <remarks>
/// Limitation: the OpenAPI library represents "not set" and "set to true" identically as
/// <c>AdditionalPropertiesAllowed = true</c> with <c>AdditionalProperties = null</c>. As a result,
/// a schema with explicit <c>additionalProperties: true</c> cannot be distinguished from one
/// where the field is simply omitted. To satisfy this rule with explicit-permissive behavior,
/// set <c>AdditionalProperties</c> to an explicit schema (e.g., <c>new OpenApiSchema()</c> or
/// a typed schema); to deny additional properties, set <c>AdditionalPropertiesAllowed = false</c>.
/// </remarks>
public sealed class SchemaAdditionalPropertiesExplicitRule : IValidationRule
{
    public string Id => "schema.additional-properties-explicit";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;

            // Only check object schemas with properties
            if (s.Type != JsonSchemaType.Object) continue;
            if (s.Properties == null || s.Properties.Count == 0) continue;

            // Skip composition schemas — allOf/anyOf/oneOf have different additionalProperties semantics
            bool isComposition = (s.AllOf?.Count > 0) || (s.AnyOf?.Count > 0) || (s.OneOf?.Count > 0);
            if (isComposition) continue;

            // Violation if additionalProperties is not explicitly set.
            // Explicit means: either AdditionalProperties has a schema, or AdditionalPropertiesAllowed is false.
            // (AdditionalPropertiesAllowed defaults to true and cannot distinguish "not set" from "set true".)
            if (s.AdditionalProperties == null && s.AdditionalPropertiesAllowed != false)
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    resolver.ForSchema(schemaId),
                    $"Schema '{schemaId}' does not explicitly set 'additionalProperties'. " +
                    "Set to false to reject extra properties, or true/a schema to allow them.");
            }
        }
    }
}
