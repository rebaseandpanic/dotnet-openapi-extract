using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.description</c>
/// Every operation must have a non-empty <c>description</c> field that is at least
/// <see cref="ValidationContext.MinDescriptionLength"/> characters long.
/// </summary>
public sealed class OperationDescriptionRule : IValidationRule
{
    public string Id => "operation.description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            var desc = operation.Description;
            var actual = desc?.Length ?? 0;
            var minLen = context.GetMinDescriptionLength(Id);
            if (string.IsNullOrWhiteSpace(desc) || actual < minLen)
            {
                var key = op.Key;
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    op.Pointer,
                    resolver.ForOperation(key),
                    $"Operation description is missing or shorter than {minLen} characters (actual: {actual}).");
            }
        }
    }
}
