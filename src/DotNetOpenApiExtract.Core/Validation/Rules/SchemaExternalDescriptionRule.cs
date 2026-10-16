using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.external-description</c>
/// <c>schema.description</c> for a component whose CLR type is declared outside the project — a NuGet
/// package or the shared framework (<see cref="SchemaOrigin"/>). The application's author cannot write
/// that description, only make the assembly's XML documentation available, so it is a warning that
/// names the assembly and how to supply its documentation.
/// </summary>
public sealed class SchemaExternalDescriptionRule : IValidationRule
{
    public string Id => "schema.external-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);
        foreach (var schemaId in SchemaDescriptionRule.Undescribed(document))
        {
            if (SchemaOrigin.ExternalAssembly(context, schemaId) is not { } assembly) continue;
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                JsonPointerHelper.ForSchema(schemaId),
                resolver.ForSchema(schemaId),
                $"Schema '{schemaId}' is missing a description. {SchemaOrigin.Hint(assembly)}");
        }
    }
}
