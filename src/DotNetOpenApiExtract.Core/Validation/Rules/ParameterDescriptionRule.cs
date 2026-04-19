using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>parameter.description</c>
/// Every operation parameter must have a non-empty description.
/// </summary>
public sealed class ParameterDescriptionRule : IValidationRule
{
    public string Id => "parameter.description";
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
                if (operation.Parameters == null) continue;

                foreach (var param in operation.Parameters.OfType<OpenApiParameter>().OrderBy(p => p.Name))
                {
                    if (string.IsNullOrWhiteSpace(param.Description))
                    {
                        var key = $"{method.ToString().ToUpperInvariant()} {path}";
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            JsonPointerHelper.ForParameter(path, method.ToString(), param.Name ?? "?"),
                            resolver.ForOperation(key),
                            $"Parameter '{param.Name}' is missing a description.");
                    }
                }
            }
        }
    }
}
