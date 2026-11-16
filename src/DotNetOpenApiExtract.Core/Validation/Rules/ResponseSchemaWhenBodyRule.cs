using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>response.schema-when-body</c>
/// If a response has content entries, each media type must describe its body: a <c>schema</c>, or
/// an <c>itemSchema</c> for a sequential media type (OpenAPI 3.2; 3.0/3.1 documents carry it as
/// <c>x-oai-itemSchema</c>, which the reader loads into the same model property). A sequential media
/// type with neither — <c>text/event-stream</c>, <c>application/jsonl</c>,
/// <c>application/x-ndjson</c>, <c>application/json-seq</c> — is not a violation: the body is a stream
/// the action writes itself, and the media type is what is known of it. The generator writes such a
/// stream that way on purpose, and warns when it does (<c>response.sequence-item-type-unknown</c>,
/// <c>response.event-stream-without-formatter</c>).
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

                    if (mediaTypeObj is OpenApiMediaType && SequentialMediaTypes.IsSequential(mediaType))
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
}
