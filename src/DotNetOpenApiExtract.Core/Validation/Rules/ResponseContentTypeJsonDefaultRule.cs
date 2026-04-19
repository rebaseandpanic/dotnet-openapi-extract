using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>response.content-type-json-default</c> (off by default, Warning severity)
/// For each response that has a content body, the response must include
/// <c>application/json</c> as one of the content-type keys.
/// </summary>
public sealed class ResponseContentTypeJsonDefaultRule : IValidationRule
{
    public string Id => "response.content-type-json-default";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

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
                    // Skip reference-typed responses — the referenced component has its own content-type check
                    // via iteration over components (when reachable).
                    if (response is not OpenApiResponse r) continue;

                    // Only check responses that actually have a content body
                    if (r.Content == null || r.Content.Count == 0) continue;

                    // Check that application/json is one of the content types
                    var hasJson = r.Content.Keys.Any(ct =>
                        ct.Equals("application/json", StringComparison.OrdinalIgnoreCase));

                    if (!hasJson)
                    {
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            JsonPointerHelper.ForResponse(path, method.ToString(), statusCode),
                            resolver.ForOperation($"{method.ToString().ToUpperInvariant()} {path}"),
                            $"Response {statusCode} for '{method.ToString().ToUpperInvariant()} {path}' " +
                            $"has content but does not include 'application/json'. " +
                            $"Content types present: {string.Join(", ", r.Content.Keys)}.");
                    }
                }
            }
        }
    }
}
