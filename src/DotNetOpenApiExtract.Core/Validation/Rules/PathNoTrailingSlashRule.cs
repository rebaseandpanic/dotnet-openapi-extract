using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>path.no-trailing-slash</c>
/// Path keys must not end with <c>/</c> except for the root path <c>/</c> itself.
/// Trailing slashes cause duplicate-URL ambiguity and break some HTTP clients.
/// Spectral <c>path-keys-no-trailing-slash</c>. Redocly <c>no-path-trailing-slash</c>.
/// </summary>
public sealed class PathNoTrailingSlashRule : IValidationRule
{
    public string Id => "path.no-trailing-slash";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        foreach (var path in document.Paths.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            // Allow the root path "/" but flag anything else ending with "/"
            if (path != "/" && path.EndsWith("/", StringComparison.Ordinal))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    $"#/paths/{JsonPointerHelper.EncodeSegment(path)}",
                    null,
                    $"Path '{path}' must not end with a trailing slash.");
            }
        }
    }
}
