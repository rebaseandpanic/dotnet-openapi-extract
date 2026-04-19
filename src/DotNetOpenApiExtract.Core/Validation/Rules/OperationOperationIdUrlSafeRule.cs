using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.operation-id-url-safe</c>
/// <c>operationId</c> must contain only URL-safe characters: <c>^[a-zA-Z0-9_-]+$</c>.
/// OperationIds with spaces or special characters break SDK method-name generation in most languages.
/// Spectral <c>operation-operationId-valid-in-url</c>. Redocly <c>operation-operationId-url-safe</c>.
/// </summary>
public sealed class OperationOperationIdUrlSafeRule : IValidationRule
{
    private static readonly Regex UrlSafeRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public string Id => "operation.operation-id-url-safe";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var opId = operation.OperationId;
                if (string.IsNullOrWhiteSpace(opId)) continue; // operation.operation-id covers missing

                if (!UrlSafeRegex.IsMatch(opId))
                {
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        null,
                        $"operationId '{opId}' contains characters that are not URL-safe. Use only a-z, A-Z, 0-9, '_', or '-'.");
                }
            }
        }
    }
}
