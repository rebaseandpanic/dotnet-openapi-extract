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
        if (document.Paths == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                if (operation.Tags == null || operation.Tags.Count == 0)
                {
                    var key = $"{method.ToString().ToUpperInvariant()} {path}";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        "Operation has no tags assigned.");
                }
            }
        }
    }
}
