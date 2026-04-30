using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Controller that declares a streaming endpoint via [Produces("text/event-stream")]
/// without specifying a typed body. Used to verify that an explicit [Produces] attribute
/// causes a Content section to be emitted even when no BodyType is available.
/// </summary>
[ApiController]
[Route("api/v1/events")]
public class EventStreamController : ControllerBase
{
    /// <summary>Subscribe to the server-sent event stream.</summary>
    /// <response code="200">Event stream started.</response>
    [HttpGet]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Subscribe()
    {
        throw new NotImplementedException();
    }
}
