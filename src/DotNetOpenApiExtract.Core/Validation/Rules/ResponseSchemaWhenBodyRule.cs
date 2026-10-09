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
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (operation.Responses == null) continue;

            foreach (var (statusCode, response) in operation.Responses.OrderBy(kv => kv.Key))
            {
                if (response is not OpenApiResponse apiResponse) continue;
                if (apiResponse.Content == null || apiResponse.Content.Count == 0) continue;

                foreach (var (mediaType, mediaTypeObj) in apiResponse.Content.OrderBy(kv => kv.Key))
                {
                    if (mediaTypeObj is not OpenApiMediaType mt || mt.Schema == null)
                    {
                        var key = op.Key;
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            $"{JsonPointerHelper.ForResponseOf(op.Pointer, statusCode)}/content/{JsonPointerHelper.EncodeSegment(mediaType)}",
                            resolver.ForOperation(key),
                            $"Response '{statusCode}' media-type '{mediaType}' has no schema defined.");
                    }
                }
            }
        }
    }
}
