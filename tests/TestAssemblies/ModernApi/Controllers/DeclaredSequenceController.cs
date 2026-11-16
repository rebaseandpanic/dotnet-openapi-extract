using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Streaming;

namespace ModernApi.Controllers;

/// <summary>
/// Streams an action writes to the response body itself, declared with sequential media types: the
/// body type comes from the attributes, not from the signature.
/// </summary>
[ApiController]
[Route("declared-sequence")]
public class DeclaredSequenceController : ControllerBase
{
    /// <summary>Newline-delimited JSON without a declared type.</summary>
    [HttpPost("untyped")]
    [Produces("application/x-ndjson")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StreamError), StatusCodes.Status422UnprocessableEntity, "application/json")]
    public Task Untyped() => Response.WriteAsync("{}\n");

    /// <summary>Newline-delimited JSON whose item type is declared for the response.</summary>
    [HttpPost("typed")]
    [ProducesResponseType(typeof(StreamItem), StatusCodes.Status200OK, "application/x-ndjson")]
    public Task Typed() => Response.WriteAsync("{}\n");

    /// <summary>JSON Lines declared as an array of items: the array is the whole stream.</summary>
    [HttpPost("array")]
    [ProducesResponseType(typeof(StreamItem[]), StatusCodes.Status200OK, "application/jsonl")]
    public Task Array() => Response.WriteAsync("{}\n");

    /// <summary>One item type declared for JSON and for JSON text sequences.</summary>
    [HttpPost("mixed")]
    [ProducesResponseType(typeof(StreamItem), StatusCodes.Status200OK, "application/json", "application/json-seq")]
    public Task Mixed() => Response.WriteAsync("{}");
}
