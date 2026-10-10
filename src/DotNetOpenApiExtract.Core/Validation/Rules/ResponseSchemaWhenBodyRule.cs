using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>response.schema-when-body</c>
/// If a response has content entries, each media type must describe its body: a <c>schema</c>, or
/// an <c>itemSchema</c> for a sequential media type (OpenAPI 3.2; 3.0/3.1 documents carry it as
/// <c>x-oai-itemSchema</c>, which the reader loads into the same model property). A
/// <c>text/event-stream</c> media type with neither is not a violation: the generator writes a
/// server-sent events stream without a typed body that way on purpose.
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
                    if (mediaTypeObj is OpenApiMediaType { Schema: not null } or OpenApiMediaType { ItemSchema: not null })
                        continue;

                    if (mediaTypeObj is OpenApiMediaType && IsEventStream(mediaType))
                        continue;

                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        $"{JsonPointerHelper.ForResponseOf(op.Pointer, statusCode)}/content/{JsonPointerHelper.EncodeSegment(mediaType)}",
                        resolver.ForOperation(op.Key),
                        $"Response '{statusCode}' media-type '{mediaType}' has no schema defined.");
                }
            }
        }
    }

    /// <summary><c>text/event-stream</c>, ignoring parameters and case.</summary>
    private static bool IsEventStream(string mediaType)
    {
        var separator = mediaType.IndexOf(';');
        var baseType = (separator < 0 ? mediaType : mediaType[..separator]).Trim();
        return string.Equals(baseType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
    }
}
