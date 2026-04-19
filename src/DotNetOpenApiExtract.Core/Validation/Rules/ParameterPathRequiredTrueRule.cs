using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>parameter.path-required-true</c>
/// OAS 3.0.3 MUST: if a parameter's <c>in</c> is <c>path</c>, its <c>required</c> property
/// is REQUIRED and its value MUST be <c>true</c>.
/// A path parameter with <c>required: false</c> (or unset) is a spec violation.
/// </summary>
public sealed class ParameterPathRequiredTrueRule : IValidationRule
{
    public string Id => "parameter.path-required-true";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item) continue;

            // Check path-level parameters
            if (item.Parameters != null)
            {
                foreach (var param in item.Parameters.OrderBy(p => p?.Name ?? ""))
                {
                    if (param?.In == ParameterLocation.Path && param.Required != true)
                    {
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            $"#/paths/{JsonPointerHelper.EncodeSegment(path)}/parameters/{JsonPointerHelper.EncodeSegment(param.Name ?? "")}",
                            null,
                            $"Path parameter '{param.Name}' in '{path}' must have required: true per OAS spec.");
                    }
                }
            }

            if (item.Operations == null) continue;

            // Check operation-level parameters
            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                if (operation.Parameters == null) continue;
                var opPtr = JsonPointerHelper.ForOperation(path, method.ToString());

                foreach (var param in operation.Parameters.OrderBy(p => p?.Name ?? ""))
                {
                    if (param?.In == ParameterLocation.Path && param.Required != true)
                    {
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            $"{opPtr}/parameters/{JsonPointerHelper.EncodeSegment(param.Name ?? "")}",
                            null,
                            $"Path parameter '{param.Name}' on {method.ToString().ToUpperInvariant()} '{path}' must have required: true per OAS spec.");
                    }
                }
            }
        }
    }
}
