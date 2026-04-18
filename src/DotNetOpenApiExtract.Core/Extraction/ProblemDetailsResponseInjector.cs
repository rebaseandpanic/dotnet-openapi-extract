using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Injects default <c>application/problem+json</c> responses into OpenAPI operations
/// for well-known error status codes when <c>AddProblemDetails()</c> is registered.
/// </summary>
public static class ProblemDetailsResponseInjector
{
    /// <summary>
    /// The HTTP status codes for which default ProblemDetails responses are injected
    /// when the action does not already document these responses.
    /// </summary>
    public static readonly IReadOnlyList<int> DefaultStatusCodes = [400, 422, 500];

    /// <summary>
    /// Injects default <c>application/problem+json</c> responses for each status code in
    /// <see cref="DefaultStatusCodes"/> that is not already documented on the operation.
    /// </summary>
    /// <param name="operation">The operation to inject responses into.</param>
    /// <param name="problemDetailsSchemaReference">
    /// A schema reference pointing to <c>#/components/schemas/ProblemDetails</c>.
    /// This is used as the response body schema to avoid inlining the full schema
    /// on every operation.
    /// </param>
    public static void Inject(OpenApiOperation operation, IOpenApiSchema problemDetailsSchemaReference)
    {
        operation.Responses ??= new OpenApiResponses();

        foreach (var statusCode in DefaultStatusCodes)
        {
            var key = statusCode.ToString();
            if (operation.Responses.ContainsKey(key))
                continue;

            operation.Responses[key] = new OpenApiResponse
            {
                Description = GetStatusDescription(statusCode),
                Content = new Dictionary<string, IOpenApiMediaType>(StringComparer.Ordinal)
                {
                    ["application/problem+json"] = new OpenApiMediaType
                    {
                        Schema = problemDetailsSchemaReference,
                    },
                },
            };
        }
    }

    private static string GetStatusDescription(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        422 => "Unprocessable Entity",
        500 => "Internal Server Error",
        _   => "Error",
    };
}
