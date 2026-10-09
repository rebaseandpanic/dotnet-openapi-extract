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
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (operation.Parameters == null) continue;

            foreach (var param in operation.Parameters.OfType<OpenApiParameter>().OrderBy(p => p.Name))
            {
                if (string.IsNullOrWhiteSpace(param.Description))
                {
                    var key = op.Key;
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForParameterOf(op.Pointer, param.Name ?? "?"),
                        resolver.ForOperation(key),
                        $"Parameter '{param.Name}' is missing a description.");
                }
            }
        }
    }
}
