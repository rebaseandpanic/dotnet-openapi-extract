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
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (!operation.Deprecated) continue;

            var desc = operation.Description ?? string.Empty;
            if (!DeprecationNotePattern.IsMatch(desc))
            {
                var key = op.Key;
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    op.Pointer,
                    resolver.ForOperation(key),
                    "Deprecated operation description must mention a replacement (keywords: \"replacement\", \"use instead\", \"removed\").");
            }
        }
    }
}
