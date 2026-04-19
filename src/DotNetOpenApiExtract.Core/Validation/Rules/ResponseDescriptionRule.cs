using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>response.description</c>
/// Every response must have a non-empty description.
/// </summary>
public sealed class ResponseDescriptionRule : IValidationRule
{
    public string Id => "response.description";
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
                if (operation.Responses == null) continue;

                foreach (var (statusCode, response) in operation.Responses.OrderBy(kv => kv.Key))
                {
                    if (response is not OpenApiResponse apiResponse) continue;

                    if (string.IsNullOrWhiteSpace(apiResponse.Description))
                    {
                        var key = $"{method.ToString().ToUpperInvariant()} {path}";
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            JsonPointerHelper.ForResponse(path, method.ToString(), statusCode),
                            resolver.ForOperation(key),
                            $"Response '{statusCode}' is missing a description.");
                    }
                }
            }
        }
    }
}
