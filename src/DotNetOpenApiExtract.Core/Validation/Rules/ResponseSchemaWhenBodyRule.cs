using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>response.schema-when-body</c>
/// If a response has content entries, each media-type must have a non-null schema.
/// </summary>
public sealed class ResponseSchemaWhenBodyRule : IValidationRule
{
    public string Id => "response.schema-when-body";
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
                    if (apiResponse.Content == null || apiResponse.Content.Count == 0) continue;

                    foreach (var (mediaType, mediaTypeObj) in apiResponse.Content.OrderBy(kv => kv.Key))
                    {
                        if (mediaTypeObj is not OpenApiMediaType mt || mt.Schema == null)
                        {
                            var key = $"{method.ToString().ToUpperInvariant()} {path}";
                            yield return new ValidationViolation(
                                Id,
                                DefaultSeverity,
                                $"{JsonPointerHelper.ForResponse(path, method.ToString(), statusCode)}/content/{JsonPointerHelper.EncodeSegment(mediaType)}",
                                resolver.ForOperation(key),
                                $"Response '{statusCode}' media-type '{mediaType}' has no schema defined.");
                        }
                    }
                }
            }
        }
    }
}
