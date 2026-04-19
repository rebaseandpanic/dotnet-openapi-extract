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
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);
        var minLen = context.GetMinDescriptionLength(Id);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                // Only check operations that have a request body
                if (operation.RequestBody == null) continue;

                // Only check inline request bodies (not references — they have their own description)
                if (operation.RequestBody is not OpenApiRequestBody requestBody) continue;

                var desc = requestBody.Description;
                var actual = desc?.Length ?? 0;

                if (string.IsNullOrWhiteSpace(desc) || actual < minLen)
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        $"{JsonPointerHelper.ForOperation(path, method.ToString())}/requestBody",
                        resolver.ForOperation(key),
                        $"Operation '{method.ToString().ToUpperInvariant()} {path}' request body description " +
                        $"is missing or shorter than {minLen} characters (actual: {actual}).");
                }
            }
        }
    }
}
