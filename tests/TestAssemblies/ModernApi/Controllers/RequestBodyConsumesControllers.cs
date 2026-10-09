using Microsoft.AspNetCore.Mvc;

namespace ModernApi.Controllers;

/// <summary>Request body sent to the consumes actions.</summary>
public class ConsumedPayload
{
    /// <summary>Payload text.</summary>
    public required string Text { get; set; }
}

/// <summary>Request bodies without a controller-level [Consumes].</summary>
[ApiController]
[Route("consumes")]
public class RequestBodyConsumesController : ControllerBase
{
    /// <summary>Body media type from the action.</summary>
    [HttpPost("action-xml")]
    [Consumes("application/xml")]
    public IActionResult ActionXml([FromBody] ConsumedPayload payload) => Ok();

    /// <summary>No [Consumes] anywhere.</summary>
    [HttpPost("default")]
    public IActionResult Default([FromBody] ConsumedPayload payload) => Ok();

    /// <summary>A URL-encoded form.</summary>
    [HttpPost("form-urlencoded")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult FormUrlEncoded([FromForm] string name, [FromForm] int age) => Ok();

    /// <summary>A form without [Consumes].</summary>
    [HttpPost("form-default")]
    public IActionResult FormDefault([FromForm] string name) => Ok();
}

/// <summary>Request bodies under a controller-level [Consumes].</summary>
[ApiController]
[Route("consumes-controller")]
[Consumes("application/xml", "text/xml")]
public class ControllerConsumesController : ControllerBase
{
    /// <summary>Body media types from the controller.</summary>
    [HttpPost("inherits")]
    public IActionResult Inherits([FromBody] ConsumedPayload payload) => Ok();

    /// <summary>The action's [Consumes] wins over the controller's.</summary>
    [HttpPost("own")]
    [Consumes("application/merge-patch+json")]
    public IActionResult Own([FromBody] ConsumedPayload payload) => Ok();
}
