using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.has-error-response</c>
/// Every operation (except those on excluded paths) must declare at least one 4xx or 5xx response.
/// </summary>
public sealed class OperationHasErrorResponseRule : IValidationRule
{
    public string Id => "operation.has-error-response";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            // Skip excluded prefixes
            if (context.ExcludedPathPrefixes.Any(prefix =>
                    path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var hasError = operation.Responses != null &&
                    operation.Responses.Keys.Any(k =>
                        (int.TryParse(k, out var code) && (code >= 400)) ||
                        k.StartsWith("4", StringComparison.Ordinal) ||
                        k.StartsWith("5", StringComparison.Ordinal) ||
                        k.Equals("default", StringComparison.OrdinalIgnoreCase));

                if (!hasError)
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        "Operation has no 4xx or 5xx error response declared.");
                }
            }
        }
    }
}
