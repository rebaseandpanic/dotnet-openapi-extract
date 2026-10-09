using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Streaming;

namespace ModernApi.Controllers;

/// <summary>Asynchronous sequences declared with sequential media types.</summary>
[ApiController]
[Route("sequential")]
public class SequentialStreamingController : ControllerBase
{
    /// <summary>JSON Lines.</summary>
    [HttpGet("jsonl")]
    [Produces("application/jsonl")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StreamError), StatusCodes.Status422UnprocessableEntity, "application/json")]
    public IAsyncEnumerable<StreamItem> Jsonl() => Items();

    /// <summary>Newline-delimited JSON.</summary>
    [HttpGet("ndjson")]
    [Produces("application/x-ndjson")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StreamError), StatusCodes.Status422UnprocessableEntity, "application/json")]
    public IAsyncEnumerable<StreamItem> Ndjson() => Items();

    /// <summary>JSON text sequences.</summary>
    [HttpGet("json-seq")]
    [Produces("application/json-seq")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StreamError), StatusCodes.Status422UnprocessableEntity, "application/json")]
    public IAsyncEnumerable<StreamItem> JsonSeq() => Items();

    /// <summary>JSON and newline-delimited JSON for one response.</summary>
    [HttpGet("mixed")]
    [Produces("application/json", "application/x-ndjson")]
    public IAsyncEnumerable<StreamItem> Mixed() => Items();

    /// <summary>Newline-delimited JSON of nullable objects.</summary>
    [HttpGet("ndjson-nullable")]
    [Produces("application/x-ndjson")]
    public IAsyncEnumerable<StreamItem?> NdjsonNullable() => Items();

    /// <summary>A sequence declared as server-sent events, which standard MVC cannot write.</summary>
    [HttpGet("event-stream")]
    [Produces("text/event-stream")]
    public IAsyncEnumerable<StreamItem> EventStream() => Items();

    /// <summary>QUERY with newline-delimited JSON.</summary>
    [AcceptVerbs("QUERY", Route = "query-ndjson")]
    [Produces("application/x-ndjson")]
    public IAsyncEnumerable<StreamItem> QueryNdjson() => Items();

    /// <summary>QUERY with a sequence declared as server-sent events.</summary>
    [AcceptVerbs("QUERY", Route = "query-event-stream")]
    [Produces("text/event-stream")]
    public IAsyncEnumerable<StreamItem> QueryEventStream() => Items();

    /// <summary>QUERY with newline-delimited JSON of a concrete polymorphic base.</summary>
    [AcceptVerbs("QUERY", Route = "query-parcels")]
    [Produces("application/x-ndjson")]
    public IAsyncEnumerable<Parcel> QueryParcels() => Parcels();

    private static async IAsyncEnumerable<StreamItem> Items()
    {
        await Task.Yield();
        yield return new StreamItem { Sequence = 1, Text = "one" };
    }

    private static async IAsyncEnumerable<Parcel> Parcels()
    {
        await Task.Yield();
        yield return new ExpressParcel { WeightGrams = 600, DeadlineHours = 24 };
    }
}
