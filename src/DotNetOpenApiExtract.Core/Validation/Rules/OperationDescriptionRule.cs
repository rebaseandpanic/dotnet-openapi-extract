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
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var desc = operation.Description;
                var actual = desc?.Length ?? 0;
                var minLen = context.GetMinDescriptionLength(Id);
                if (string.IsNullOrWhiteSpace(desc) || actual < minLen)
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        $"Operation description is missing or shorter than {minLen} characters (actual: {actual}).");
                }
            }
        }
    }
}
