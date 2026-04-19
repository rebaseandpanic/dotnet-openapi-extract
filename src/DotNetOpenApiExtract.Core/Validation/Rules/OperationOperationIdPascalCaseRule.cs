using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.operation-id-pascal-case</c> (off by default, Warning severity)
/// Every operation's <c>operationId</c> must match PascalCase: starts with an uppercase letter,
/// followed by alphanumeric characters only (no underscores, hyphens, or spaces).
/// Operations without an <c>operationId</c> are skipped (that is <c>operation.operation-id</c>'s job).
/// </summary>
public sealed class OperationOperationIdPascalCaseRule : IValidationRule
{
    public string Id => "operation.operation-id-pascal-case";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    private static readonly Regex PascalCaseRegex =
        new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var opId = operation.OperationId;

                // Skip operations without an operationId — that's operation.operation-id's job
                if (string.IsNullOrWhiteSpace(opId)) continue;

                if (!PascalCaseRegex.IsMatch(opId))
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        $"Operation '{method.ToString().ToUpperInvariant()} {path}' has operationId '{opId}' " +
                        $"which is not PascalCase (must match ^[A-Z][A-Za-z0-9]*$).");
                }
            }
        }
    }
}
