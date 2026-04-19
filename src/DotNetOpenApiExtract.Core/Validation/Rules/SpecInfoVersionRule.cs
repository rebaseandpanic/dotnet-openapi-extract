using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.info-version</c>
/// The document <c>info.version</c> must be present and non-empty.
/// Both OAS 3.0.3 and 3.1.0 list this as a REQUIRED field.
/// </summary>
public sealed class SpecInfoVersionRule : IValidationRule
{
    public string Id => "spec.info-version";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(document.Info?.Version))
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                "#/info/version",
                null,
                "info.version is required and must not be empty.");
        }
    }
}
