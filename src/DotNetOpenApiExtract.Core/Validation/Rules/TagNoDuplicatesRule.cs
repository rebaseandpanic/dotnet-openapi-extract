using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>tag.no-duplicates</c>
/// The top-level <c>tags</c> array must not contain two entries with the same name.
/// Spectral <c>openapi-tags-uniqueness</c> error. Redocly <c>no-duplicated-tag-names</c> error.
/// </summary>
public sealed class TagNoDuplicatesRule : IValidationRule
{
    public string Id => "tag.no-duplicates";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Tags == null || document.Tags.Count == 0) yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();

        foreach (var tag in document.Tags)
        {
            if (tag?.Name == null) continue;
            if (!seen.Add(tag.Name))
                duplicates.Add(tag.Name);
        }

        foreach (var name in duplicates.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                "#/tags",
                null,
                $"Tag '{name}' is declared more than once in the top-level tags array.");
        }
    }
}
