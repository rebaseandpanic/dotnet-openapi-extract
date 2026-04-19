using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>path.no-identical</c>
/// Duplicate path strings in the document produce ambiguous routing.
/// <para>
/// Note: <see cref="OpenApiPaths"/> is a <see cref="Dictionary{TKey,TValue}"/> so in-memory
/// duplicates are structurally impossible. This rule fires primarily in the standalone
/// <c>validate</c> subcommand when parsing spec files that have been hand-edited or
/// serialized from a non-deduplicating source.
/// </para>
/// Redocly <c>no-identical-paths</c> error.
/// </summary>
public sealed class PathNoIdenticalRule : IValidationRule
{
    public string Id => "path.no-identical";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        foreach (var path in document.Paths.Keys)
        {
            if (!seen.Add(path))
                duplicates.Add(path);
        }

        foreach (var path in duplicates.OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                $"#/paths/{JsonPointerHelper.EncodeSegment(path)}",
                null,
                $"Path '{path}' is declared more than once. Duplicate paths produce ambiguous routing.");
        }
    }
}
