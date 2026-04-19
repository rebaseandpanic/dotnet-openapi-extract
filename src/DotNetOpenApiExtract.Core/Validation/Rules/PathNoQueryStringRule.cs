using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>path.no-query-string</c>
/// Path keys must not contain <c>?</c>. Query parameters in paths break routing in all major
/// frameworks and are invalid per OAS spec.
/// Spectral <c>path-not-include-query</c>. Redocly <c>path-not-include-query</c>.
/// </summary>
public sealed class PathNoQueryStringRule : IValidationRule
{
    public string Id => "path.no-query-string";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        foreach (var path in document.Paths.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (path.Contains('?'))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    $"#/paths/{JsonPointerHelper.EncodeSegment(path)}",
                    null,
                    $"Path '{path}' contains a query string ('?'). Query parameters must be defined as operation parameters, not embedded in the path.");
            }
        }
    }
}
