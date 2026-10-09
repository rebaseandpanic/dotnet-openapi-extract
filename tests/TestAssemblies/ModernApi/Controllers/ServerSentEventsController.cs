using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Streaming;

namespace ModernApi.Controllers;

/// <summary>Server-sent events written by <see cref="ServerSentEventsResult{T}"/>.</summary>
[ApiController]
[Route("sse")]
public class ServerSentEventsController : ControllerBase
{
    /// <summary>Events whose data is a JSON object.</summary>
    [HttpGet("items")]
    public ServerSentEventsResult<StreamItem> Items() => TypedResults.ServerSentEvents(StreamItems(), "item");

    /// <summary>Events whose data is text written as is.</summary>
    [HttpGet("text")]
    public ServerSentEventsResult<string> Text() => TypedResults.ServerSentEvents(Lines());

    /// <summary>Events whose data is bytes written as is.</summary>
    [HttpGet("bytes")]
    public ServerSentEventsResult<byte[]> Bytes() => TypedResults.ServerSentEvents(Chunks());

    /// <summary>Events whose data is a recursive object.</summary>
    [HttpGet("tree")]
    public ServerSentEventsResult<TreeEvent> Tree() => TypedResults.ServerSentEvents(Trees());

    private static async IAsyncEnumerable<StreamItem> StreamItems()
    {
        await Task.Yield();
        yield return new StreamItem { Sequence = 1, Text = "one" };
    }

    private static async IAsyncEnumerable<string> Lines()
    {
        await Task.Yield();
        yield return "line";
    }

    private static async IAsyncEnumerable<byte[]> Chunks()
    {
        await Task.Yield();
        yield return [1, 2, 3];
    }

    private static async IAsyncEnumerable<TreeEvent> Trees()
    {
        await Task.Yield();
        yield return new TreeEvent { Label = "root", Children = [new TreeEvent { Label = "leaf" }] };
    }
}
