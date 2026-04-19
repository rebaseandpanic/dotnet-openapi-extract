using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.deprecated-has-note</c>
/// If an operation is marked deprecated, its description must mention a replacement or removal note.
/// The description must contain at least one of: "replacement", "use instead", "removed" (case-insensitive).
/// </summary>
public sealed class OperationDeprecatedHasNoteRule : IValidationRule
{
    public string Id => "operation.deprecated-has-note";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    private static readonly Regex DeprecationNotePattern = new(
        @"replacement|use instead|removed",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                if (!operation.Deprecated) continue;

                var desc = operation.Description ?? string.Empty;
                if (!DeprecationNotePattern.IsMatch(desc))
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        "Deprecated operation description must mention a replacement (keywords: \"replacement\", \"use instead\", \"removed\").");
                }
            }
        }
    }
}
