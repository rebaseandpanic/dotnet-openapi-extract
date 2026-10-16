using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.description</c>
/// Every object schema in <c>components/schemas</c> must have a non-empty description.
/// Primitives and non-object schemas are skipped (only type=Object or composite schemas are checked).
/// A component whose CLR type comes from outside the project (<see cref="SchemaOrigin"/>) is left to
/// <see cref="SchemaExternalDescriptionRule"/>.
/// </summary>
public sealed class SchemaDescriptionRule : IValidationRule
{
    public string Id => "schema.description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);
        foreach (var schemaId in Undescribed(document))
        {
            if (SchemaOrigin.ExternalAssembly(context, schemaId) != null) continue;
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                JsonPointerHelper.ForSchema(schemaId),
                resolver.ForSchema(schemaId),
                $"Schema '{schemaId}' is missing a description.");
        }
    }

    /// <summary>The ids of the object and composite component schemas without a description, in order.</summary>
    internal static IEnumerable<string> Undescribed(OpenApiDocument document)
    {
        if (document.Components?.Schemas == null) yield break;

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;

            // Only check object and composite schemas
            bool isObjectOrComposite = s.Type == JsonSchemaType.Object
                || s.AllOf?.Count > 0
                || s.AnyOf?.Count > 0
                || s.OneOf?.Count > 0;

            if (isObjectOrComposite && string.IsNullOrWhiteSpace(s.Description))
                yield return schemaId;
        }
    }
}
