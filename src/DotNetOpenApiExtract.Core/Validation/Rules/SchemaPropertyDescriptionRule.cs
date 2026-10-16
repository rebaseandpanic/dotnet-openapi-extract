using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.property-description</c>
/// Each property within an object schema in <c>components/schemas</c> must have a non-empty description.
/// <para>
/// A property that is a reference is read by its own keywords (<see cref="ReferencePlace.OwnSiblings"/>):
/// the 3.0 <c>allOf</c> wrapper and the siblings of <c>$ref</c> in 3.1+ alike; the referenced component
/// is not read, and a bare reference is skipped.
/// </para>
/// <para>
/// A property declared outside the project — by a package or framework type, a component's own or a
/// base type of a project type (<see cref="SchemaOrigin"/>) — is left to
/// <see cref="SchemaExternalPropertyDescriptionRule"/>.
/// </para>
/// </summary>
public sealed class SchemaPropertyDescriptionRule : IValidationRule
{
    public string Id => "schema.property-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);
        foreach (var (schemaId, propName) in Undescribed(document))
        {
            if (SchemaOrigin.ExternalAssembly(context, schemaId, propName) != null) continue;
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                resolver.ForSchemaProperty(schemaId, propName),
                $"Property '{propName}' in schema '{schemaId}' is missing a description.");
        }
    }

    /// <summary>The properties of component schemas without a description of their own, in order.</summary>
    internal static IEnumerable<(string SchemaId, string Property)> Undescribed(OpenApiDocument document)
    {
        if (document.Components?.Schemas == null) yield break;

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s || s.Properties == null) continue;

            foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key))
            {
                if (ReferencePlace.OwnSiblings(propSchema) is not { } prop) continue;
                if (string.IsNullOrWhiteSpace(prop.Description))
                    yield return (schemaId, propName);
            }
        }
    }
}
