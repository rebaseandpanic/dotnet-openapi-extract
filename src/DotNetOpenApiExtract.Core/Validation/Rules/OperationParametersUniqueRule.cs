using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.parameters-unique</c>
/// No operation may declare two parameters with the same (<c>name</c>, <c>in</c>) combination.
/// OAS 3.0.3 requires parameter uniqueness within an operation. Redocly <c>operation-parameters-unique</c> error.
/// </summary>
public sealed class OperationParametersUniqueRule : IValidationRule
{
    public string Id => "operation.parameters-unique";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var opPtr = JsonPointerHelper.ForOperation(path, method.ToString());

                // Merge path-level parameters with operation-level parameters.
                // OAS requires (name, in) uniqueness across both; operation-level may
                // override path-level with the same (name, in) — we flag that as a duplicate
                // (matches Redocly/Spectral linter strictness, stricter than the OAS spec itself).
                var pathParams = item.Parameters ?? Enumerable.Empty<IOpenApiParameter>();
                var operationParams = operation.Parameters ?? Enumerable.Empty<IOpenApiParameter>();
                var merged = pathParams.Concat(operationParams).ToList();

                if (merged.Count == 0) continue;

                var seen = new HashSet<(string name, ParameterLocation? location)>();

                foreach (var param in merged)
                {
                    if (param == null) continue;
                    var key = (param.Name ?? "", param.In);
                    if (!seen.Add(key))
                    {
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            $"{opPtr}/parameters",
                            null,
                            $"Operation {method.ToString().ToUpperInvariant()} '{path}' has duplicate parameter '{param.Name}' with in:{param.In}.");
                    }
                }
            }
        }
    }
}
