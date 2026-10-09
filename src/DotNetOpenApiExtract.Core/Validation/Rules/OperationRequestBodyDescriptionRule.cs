using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.request-body-description</c>
/// Every operation with a request body must have a non-empty description that meets the
/// effective minimum description length (global or per-rule override).
/// </summary>
public sealed class OperationRequestBodyDescriptionRule : IValidationRule
{
    public string Id => "operation.request-body-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);
        var minLen = context.GetMinDescriptionLength(Id);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var path = op.Name;
            var operation = op.Operation;
            // Only check operations that have a request body
            if (operation.RequestBody == null) continue;

            // Only check inline request bodies (not references — they have their own description)
            if (operation.RequestBody is not OpenApiRequestBody requestBody) continue;

            var desc = requestBody.Description;
            var actual = desc?.Length ?? 0;

            if (string.IsNullOrWhiteSpace(desc) || actual < minLen)
            {
                var key = op.Key;
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    $"{op.Pointer}/requestBody",
                    resolver.ForOperation(key),
                    $"Operation '{op.MethodName} {path}' request body description " +
                    $"is missing or shorter than {minLen} characters (actual: {actual}).");
            }
        }
    }
}
