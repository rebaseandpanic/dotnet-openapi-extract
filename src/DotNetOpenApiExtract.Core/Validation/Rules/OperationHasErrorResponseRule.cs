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
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            if (op.Root == OperationRoot.Paths && context.ExcludedPathPrefixes.Any(prefix =>
                    op.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;

            var operation = op.Operation;
            var hasError = operation.Responses != null &&
                operation.Responses.Keys.Any(k =>
                    (int.TryParse(k, out var code) && (code >= 400)) ||
                    k.StartsWith("4", StringComparison.Ordinal) ||
                    k.StartsWith("5", StringComparison.Ordinal) ||
                    k.Equals("default", StringComparison.OrdinalIgnoreCase));

            if (!hasError)
            {
                var key = op.Key;
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    op.Pointer,
                    resolver.ForOperation(key),
                    "Operation has no 4xx or 5xx error response declared.");
            }
        }
    }
}
