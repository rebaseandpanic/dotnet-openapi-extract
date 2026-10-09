using System.Net.ServerSentEvents;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Streaming;

namespace ModernApi.Controllers;

/// <summary>Asynchronous sequences returned by controllers (written by MVC as JSON arrays).</summary>
[ApiController]
[Route("streaming")]
public class StreamingController : ControllerBase
{
    /// <summary>Plain asynchronous sequence.</summary>
    [HttpGet("plain")]
    public IAsyncEnumerable<StreamItem> Plain() => Items();

    /// <summary>Sequence inside a task.</summary>
    [HttpGet("task")]
    public Task<IAsyncEnumerable<StreamItem>> InTask() => Task.FromResult(Items());

    /// <summary>Sequence inside a value task.</summary>
    [HttpGet("value-task")]
    public ValueTask<IAsyncEnumerable<StreamItem>> InValueTask() => ValueTask.FromResult(Items());

    /// <summary>Sequence inside an action result.</summary>
    [HttpGet("action-result")]
    public ActionResult<IAsyncEnumerable<StreamItem>> InActionResult() => new(Items());

    /// <summary>User type implementing the sequence interface.</summary>
    [HttpGet("custom")]
    public StreamItemSequence Custom() => new([]);

    /// <summary>Sequence type declared by an attribute.</summary>
    [HttpGet("declared")]
    [ProducesResponseType(typeof(IAsyncEnumerable<StreamItem>), StatusCodes.Status200OK)]
    public IActionResult Declared() => Ok(Items());

    /// <summary>Sequence of nullable integers.</summary>
    [HttpGet("nullable-ints")]
    public ActionResult<IAsyncEnumerable<int?>> NullableInts() => new(Numbers());

    /// <summary>Sequence of server-sent event items, which MVC writes as a JSON array of objects.</summary>
    [HttpGet("sse-items")]
    public IAsyncEnumerable<SseItem<StreamItem>> SseItems() => Events();

    /// <summary>Sequence of nullable strings inside an action result.</summary>
    [HttpGet("nullable-strings")]
    public ActionResult<IAsyncEnumerable<string?>> NullableStrings() => new(Strings());

    /// <summary>Sequence of nullable objects inside a task.</summary>
    [HttpGet("nullable-items")]
    public Task<IAsyncEnumerable<StreamItem?>> NullableItems() => Task.FromResult<IAsyncEnumerable<StreamItem?>>(Items());

    /// <summary>Sequence of nullable strings inside a value task (a value type takes no nullable byte).</summary>
    [HttpGet("nullable-strings-value-task")]
    public ValueTask<IAsyncEnumerable<string?>> NullableStringsInValueTask() => ValueTask.FromResult(Strings());

#nullable disable
    /// <summary>Sequence of objects without nullable annotations (oblivious).</summary>
    [HttpGet("oblivious-items")]
    public IAsyncEnumerable<StreamItem> ObliviousItems() => Items();
#nullable restore

    private static async IAsyncEnumerable<string?> Strings()
    {
        await Task.Yield();
        yield return null;
    }

    private static async IAsyncEnumerable<StreamItem> Items()
    {
        await Task.Yield();
        yield return new StreamItem { Sequence = 1, Text = "one" };
    }

    private static async IAsyncEnumerable<int?> Numbers()
    {
        await Task.Yield();
        yield return 1;
        yield return null;
    }

    private static async IAsyncEnumerable<SseItem<StreamItem>> Events()
    {
        await Task.Yield();
        yield return new SseItem<StreamItem>(new StreamItem { Sequence = 1, Text = "one" });
    }
}
