using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.info-title</c>
/// The document <c>info.title</c> must be non-empty.
/// </summary>
public sealed class SpecInfoTitleRule : IValidationRule
{
    public string Id => "spec.info-title";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(document.Info?.Title))
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                JsonPointerHelper.ForInfo(),
                null,
                "Document info.title is missing or empty.");
        }
    }
}
