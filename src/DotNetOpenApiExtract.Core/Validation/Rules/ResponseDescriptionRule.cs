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
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (operation.Responses == null) continue;

            foreach (var (statusCode, response) in operation.Responses.OrderBy(kv => kv.Key))
            {
                if (response is not OpenApiResponse apiResponse) continue;

                if (string.IsNullOrWhiteSpace(apiResponse.Description))
                {
                    var key = op.Key;
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForResponseOf(op.Pointer, statusCode),
                        resolver.ForOperation(key),
                        $"Response '{statusCode}' is missing a description.");
                }
            }
        }
    }
}
