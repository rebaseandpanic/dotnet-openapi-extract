namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Sequential media types (OpenAPI 3.2 §4.14.3.1): content that is a repeating structure of items,
/// without a header, footer or envelope. Their items are described by <c>itemSchema</c>; a
/// <c>schema</c> applies to the whole content, read as an array of the items in order.
/// </summary>
internal static class SequentialMediaTypes
{
    /// <summary>Sequential media types whose items are JSON texts, as .NET formatters produce them.</summary>
    private static readonly HashSet<string> JsonItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/jsonl",
        "application/x-ndjson",
        "application/json-seq",
    };

    /// <summary>The media type of server-sent events.</summary>
    public const string EventStream = "text/event-stream";

    /// <summary>The media type without parameters (<c>application/x-ndjson; charset=utf-8</c> → <c>application/x-ndjson</c>).</summary>
    public static string BaseMediaType(string contentType)
    {
        var separator = contentType.IndexOf(';');
        return (separator < 0 ? contentType : contentType[..separator]).Trim();
    }

    /// <summary>
    /// Whether <paramref name="contentType"/> (parameters and case ignored) is a sequential media type
    /// of JSON items: <c>application/jsonl</c>, <c>application/x-ndjson</c>, <c>application/json-seq</c>.
    /// </summary>
    public static bool HasJsonItems(string contentType) => JsonItems.Contains(BaseMediaType(contentType));

    /// <summary>Whether <paramref name="contentType"/> (parameters and case ignored) is <c>text/event-stream</c>.</summary>
    public static bool IsEventStream(string contentType) =>
        string.Equals(BaseMediaType(contentType), EventStream, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="contentType"/> is one of the sequential media types the generator
    /// describes: the ones of JSON items and <c>text/event-stream</c>.
    /// </summary>
    public static bool IsSequential(string contentType) => HasJsonItems(contentType) || IsEventStream(contentType);
}
