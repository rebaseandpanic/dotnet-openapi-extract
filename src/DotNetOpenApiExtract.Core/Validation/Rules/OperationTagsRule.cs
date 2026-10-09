using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.tags</c>
/// Every operation must have at least one tag assigned.
/// </summary>
public sealed class OperationTagsRule : IValidationRule
{
    public string Id => "operation.tags";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (operation.Tags == null || operation.Tags.Count == 0)
            {
                var key = op.Key;
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    op.Pointer,
                    resolver.ForOperation(key),
                    "Operation has no tags assigned.");
            }
        }
    }
}
