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
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (string.IsNullOrWhiteSpace(operation.OperationId))
            {
                var key = op.Key;
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    op.Pointer,
                    resolver.ForOperation(key),
                    $"Operation is missing an operationId.");
            }
        }
    }
}
