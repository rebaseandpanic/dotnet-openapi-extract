using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>path.no-empty-declaration</c>
/// Path template variables must not be empty. A path containing <c>{}</c> is structurally
/// invalid per OAS spec. Spectral <c>path-declarations-must-exist</c>.
/// </summary>
public sealed class PathNoEmptyDeclarationRule : IValidationRule
{
    public string Id => "path.no-empty-declaration";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        foreach (var path in document.Paths.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (path.Contains("{}", StringComparison.Ordinal))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    $"#/paths/{JsonPointerHelper.EncodeSegment(path)}",
                    null,
                    $"Path '{path}' contains an empty path variable declaration '{{}}'.");
            }
        }
    }
}
