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
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var path = op.Name;
            var operation = op.Operation;
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
                        JsonPointerHelper.ForResponseOf(op.Pointer, statusCode),
                        resolver.ForOperation(op.Key),
                        $"Response {statusCode} for '{op.MethodName} {path}' " +
                        $"has content but does not include 'application/json'. " +
                        $"Content types present: {string.Join(", ", r.Content.Keys)}.");
                }
            }
        }
    }
}
