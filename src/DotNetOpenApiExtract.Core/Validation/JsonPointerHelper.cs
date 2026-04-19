namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Helpers for building RFC 6901 JSON Pointer strings.
/// </summary>
internal static class JsonPointerHelper
{
    /// <summary>
    /// Encodes a single JSON Pointer segment: escapes <c>~</c> → <c>~0</c> then <c>/</c> → <c>~1</c>
    /// (order matters per RFC 6901 §3).
    /// </summary>
    public static string EncodeSegment(string segment)
        => segment.Replace("~", "~0", StringComparison.Ordinal)
                  .Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>
    /// Builds a JSON Pointer for an operation: <c>#/paths/{path}/{method}</c>.
    /// </summary>
    public static string ForOperation(string path, string method)
        => $"#/paths/{EncodeSegment(path)}/{method.ToLowerInvariant()}";

    /// <summary>
    /// Builds a JSON Pointer for a parameter on an operation.
    /// </summary>
    public static string ForParameter(string path, string method, string paramName)
        => $"{ForOperation(path, method)}/parameters/{EncodeSegment(paramName)}";

    /// <summary>
    /// Builds a JSON Pointer for a response on an operation.
    /// </summary>
    public static string ForResponse(string path, string method, string statusCode)
        => $"{ForOperation(path, method)}/responses/{statusCode}";

    /// <summary>
    /// Builds a JSON Pointer for a component schema.
    /// </summary>
    public static string ForSchema(string schemaId)
        => $"#/components/schemas/{EncodeSegment(schemaId)}";

    /// <summary>
    /// Builds a JSON Pointer for a property within a component schema.
    /// </summary>
    public static string ForSchemaProperty(string schemaId, string propertyName)
        => $"#/components/schemas/{EncodeSegment(schemaId)}/properties/{EncodeSegment(propertyName)}";

    /// <summary>
    /// Builds a JSON Pointer for a security scheme.
    /// </summary>
    public static string ForSecurityScheme(string schemeName)
        => $"#/components/securitySchemes/{EncodeSegment(schemeName)}";

    /// <summary>
    /// Builds a JSON Pointer for the document info block.
    /// </summary>
    public static string ForInfo() => "#/info";
}
