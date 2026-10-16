using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.external-property-description</c>
/// <c>schema.property-description</c> for the properties of a component whose CLR type is declared
/// outside the project — a NuGet package or the shared framework (<see cref="SchemaOrigin"/>): a
/// warning that names the assembly and how to supply its XML documentation.
/// </summary>
public sealed class SchemaExternalPropertyDescriptionRule : IValidationRule
{
    public string Id => "schema.external-property-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);
        foreach (var (schemaId, propName) in SchemaPropertyDescriptionRule.Undescribed(document))
        {
            if (SchemaOrigin.ExternalAssembly(context, schemaId) is not { } assembly) continue;
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                resolver.ForSchemaProperty(schemaId, propName),
                $"Property '{propName}' in schema '{schemaId}' is missing a description. {SchemaOrigin.Hint(assembly)}");
        }
    }
}
