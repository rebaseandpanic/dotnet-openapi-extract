using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Provides the RFC 7807 <c>ProblemDetails</c> OpenAPI schema definition.
/// </summary>
internal static class ProblemDetailsSchema
{
    /// <summary>
    /// The canonical component-schema identifier used in <c>#/components/schemas/ProblemDetails</c>.
    /// </summary>
    public const string SchemaId = "ProblemDetails";

    /// <summary>
    /// Returns an <see cref="OpenApiSchema"/> that matches the RFC 7807 ProblemDetails structure.
    /// </summary>
    /// <returns>
    /// An inline <see cref="OpenApiSchema"/> with properties for <c>type</c>, <c>title</c>,
    /// <c>status</c>, <c>detail</c>, and <c>instance</c>. Additional properties are allowed
    /// to support RFC 7807 extension members.
    /// </returns>
    public static OpenApiSchema CreateSchema() => new()
    {
        Type = JsonSchemaType.Object,
        Description = "A machine-readable format for specifying errors in HTTP API responses based on RFC 7807.",
        AdditionalProperties = new OpenApiSchema(), // RFC 7807 §3.2 allows any JSON type for extensions
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["type"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uri",
                Description = "A URI reference that identifies the problem type.",
            },
            ["title"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "A short, human-readable summary of the problem type.",
            },
            ["status"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Description = "The HTTP status code.",
            },
            ["detail"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "A human-readable explanation specific to this occurrence of the problem.",
            },
            ["instance"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uri",
                Description = "A URI reference that identifies the specific occurrence of the problem.",
            },
        },
    };
}
