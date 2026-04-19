using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.operation-id</c>
/// Every operation must have a non-empty <c>operationId</c> field.
/// </summary>
public sealed class OperationOperationIdRule : IValidationRule
{
    public string Id => "operation.operation-id";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                if (string.IsNullOrWhiteSpace(operation.OperationId))
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        $"Operation is missing an operationId.");
                }
            }
        }
    }
}
