using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.no-required-undefined</c>
/// Properties listed in <c>required[]</c> must be defined in <c>properties</c>.
/// A required property that is not in <c>properties</c> is a structural error that breaks
/// validation tools and code generators.
/// Redocly <c>no-required-schema-properties-undefined</c> warn.
/// <para>
/// Note: this is the complement to <c>schema.required-consistency</c> (which checks that
/// defined non-nullable properties appear in <c>required[]</c>). Both directions are needed.
/// </para>
/// </summary>
public sealed class SchemaNoRequiredUndefinedRule : IValidationRule
{
    public string Id => "schema.no-required-undefined";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;
            if (s.Required == null || s.Required.Count == 0) continue;

            var definedProps = new HashSet<string>(
                s.Properties?.Keys ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            foreach (var requiredProp in s.Required.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!definedProps.Contains(requiredProp))
                {
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForSchema(schemaId),
                        null,
                        $"Property '{requiredProp}' is listed in the required array of schema '{schemaId}' but is not defined in properties.");
                }
            }
        }
    }
}
