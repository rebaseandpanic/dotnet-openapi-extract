using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DotNetOpenApiExtract.Core.Tests.Conformance;

/// <summary>One event of a <c>text/event-stream</c> response, as read from the wire.</summary>
/// <param name="EventType">The <c>event</c> field, or null when absent.</param>
/// <param name="Id">The <c>id</c> field, or null when absent.</param>
/// <param name="Retry">The <c>retry</c> field in milliseconds, or null when absent.</param>
/// <param name="Data">The <c>data</c> lines joined with <c>\n</c>.</param>
internal sealed record SseEvent(string? EventType, string? Id, int? Retry, string Data)
{
    /// <summary>
    /// The event carries no data: a null item, which ASP.NET Core writes as an empty
    /// <c>data</c> field. Such data is not JSON and is never parsed or checked against <c>contentSchema</c>.
    /// </summary>
    public bool IsEmptyData => Data.Length == 0;

    /// <summary>The data parsed as JSON; only for events that are not <see cref="IsEmptyData"/>.</summary>
    public JsonNode? ParseJsonData() =>
        IsEmptyData
            ? throw new InvalidOperationException("The event has empty data; it is not JSON content.")
            : JsonNode.Parse(Data);

    /// <summary>The event as an object with a string <c>data</c>, the shape an <c>itemSchema</c> describes.</summary>
    public JsonObject ToEventObject()
    {
        var obj = new JsonObject { ["data"] = Data };
        if (EventType != null) obj["event"] = EventType;
        if (Id != null) obj["id"] = Id;
        if (Retry != null) obj["retry"] = Retry;
        return obj;
    }
}

/// <summary>Response of a server-sent events result: its content type and its events.</summary>
internal sealed record SseResponse(string? ContentType, IReadOnlyList<SseEvent> Events);

/// <summary>
/// Runs a real <see cref="ServerSentEventsResult{T}"/> on an in-memory <see cref="DefaultHttpContext"/>
/// with the HTTP JSON options registered in services, and parses the stream by the SSE wire format.
/// </summary>
internal static class SseWire
{
    /// <summary>Executes <paramref name="result"/> and returns what it wrote.</summary>
    /// <param name="result">The result produced by the fixture's code path.</param>
    /// <param name="configureHttpJson">The fixture's HTTP JSON settings (<c>ConfigureHttpJsonOptions</c>).</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    public static async Task<SseResponse> ExecuteAsync<T>(
        ServerSentEventsResult<T> result,
        Action<HttpJsonOptions>? configureHttpJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HttpJsonOptions>(options => configureHttpJson?.Invoke(options));

        await using var provider = services.BuildServiceProvider();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            RequestAborted  = cancellationToken,
        };
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        return new SseResponse(context.Response.ContentType, Parse(Encoding.UTF8.GetString(body.ToArray())));
    }

    /// <summary>
    /// Parses <c>text/event-stream</c> text: events end with a blank line; <c>event</c>, <c>id</c>,
    /// <c>retry</c> and <c>data</c> fields; one optional space after the colon is not part of the value;
    /// comment lines (starting with <c>:</c>) are ignored.
    /// </summary>
    private static IReadOnlyList<SseEvent> Parse(string stream)
    {
        var events = new List<SseEvent>();
        string? eventType = null, id = null;
        int? retry = null;
        List<string>? data = null;

        foreach (var rawLine in stream.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.Length == 0)
            {
                if (data != null)
                    events.Add(new SseEvent(eventType, id, retry, string.Join('\n', data)));
                (eventType, id, retry, data) = (null, null, null, null);
                continue;
            }

            if (rawLine[0] == ':')
                continue;

            var colon = rawLine.IndexOf(':');
            var field = colon < 0 ? rawLine : rawLine[..colon];
            var value = colon < 0 ? string.Empty : rawLine[(colon + 1)..];
            if (value.StartsWith(' '))
                value = value[1..];

            switch (field)
            {
                case "event": eventType = value; break;
                case "id":    id = value; break;
                case "retry": retry = int.TryParse(value, out var ms) ? ms : null; break;
                case "data":  (data ??= []).Add(value); break;
            }
        }

        return events;
    }
}
