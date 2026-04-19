using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>tag.description</c>
/// Each entry in the top-level <c>tags</c> array should have a non-empty <c>description</c>.
/// Undescribed tags produce poor navigation in Redoc and SwaggerUI.
/// <para>
/// This rule is <b>off by default</b>. Enable with <c>--enable-rule tag.description</c>.
/// </para>
/// Spectral <c>tag-description</c> recommended. Redocly <c>tag-description</c> warn.
/// </summary>
public sealed class TagDescriptionRule : IValidationRule
{
    public string Id => "tag.description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Tags == null || document.Tags.Count == 0) yield break;

        foreach (var tag in document.Tags.OrderBy(t => t?.Name ?? ""))
        {
            if (tag?.Name == null) continue;
            if (string.IsNullOrWhiteSpace(tag.Description))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    "#/tags",
                    null,
                    $"Tag '{tag.Name}' has no description. Add a description to improve API portal navigation.");
            }
        }
    }
}
