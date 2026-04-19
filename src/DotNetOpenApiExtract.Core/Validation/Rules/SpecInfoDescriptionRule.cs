using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.info-description</c>
/// The document <c>info.description</c> must be non-empty and at least
/// <see cref="ValidationContext.MinDescriptionLength"/> characters long.
/// </summary>
public sealed class SpecInfoDescriptionRule : IValidationRule
{
    public string Id => "spec.info-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var desc = document.Info?.Description;
        var actual = desc?.Length ?? 0;
        var minLen = context.GetMinDescriptionLength(Id);

        if (string.IsNullOrWhiteSpace(desc) || actual < minLen)
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                JsonPointerHelper.ForInfo(),
                null,
                $"Document info.description is missing or shorter than {minLen} characters (actual: {actual}).");
        }
    }
}
