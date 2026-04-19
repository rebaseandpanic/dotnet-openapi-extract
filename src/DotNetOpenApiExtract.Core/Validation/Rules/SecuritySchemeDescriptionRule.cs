using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>security.scheme-description</c>
/// Each security scheme in <c>components/securitySchemes</c> must have a non-empty description.
/// </summary>
public sealed class SecuritySchemeDescriptionRule : IValidationRule
{
    public string Id => "security.scheme-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.SecuritySchemes == null) yield break;

        foreach (var (schemeName, scheme) in document.Components.SecuritySchemes.OrderBy(kv => kv.Key))
        {
            if (scheme is not OpenApiSecurityScheme concreteScheme) continue;

            if (string.IsNullOrWhiteSpace(concreteScheme.Description))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSecurityScheme(schemeName),
                    null,
                    $"Security scheme '{schemeName}' is missing a description.");
            }
        }
    }
}
