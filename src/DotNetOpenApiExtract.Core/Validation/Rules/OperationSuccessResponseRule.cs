using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.success-response</c>
/// Each operation must declare at least one 2xx or 3xx response.
/// Spectral <c>operation-success-response</c> recommended. Redocly <c>operation-2xx-response</c> warn.
/// Operations that declare only error responses confuse API consumers and code generators.
/// </summary>
/// <remarks>
/// The <c>default</c> response key is not counted as a success response. Operations using
/// <c>default</c> as their sole declared response will trigger this rule. This matches Spectral
/// behavior; Redocly treats <c>default</c> as sufficient.
/// </remarks>
public sealed class OperationSuccessResponseRule : IValidationRule
{
    public string Id => "operation.success-response";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (context.ExcludedPathPrefixes.Any(prefix =>
                    path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var hasSuccess = operation.Responses != null &&
                    operation.Responses.Keys.Any(k =>
                        (int.TryParse(k, out var code) && (code >= 200 && code < 400)) ||
                        k.StartsWith("2", StringComparison.Ordinal) ||
                        k.StartsWith("3", StringComparison.Ordinal));

                if (!hasSuccess)
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        "Operation has no 2xx or 3xx success response declared.");
                }
            }
        }
    }
}
