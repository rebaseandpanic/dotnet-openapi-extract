using Microsoft.AspNetCore.Mvc;
using ModernApi.Attributes;

namespace ModernApi.Controllers;

/// <summary>Actions declaring their HTTP methods in every supported form.</summary>
[ApiController]
[Route("http-methods")]
public class HttpMethodsController : ControllerBase
{
    /// <summary>Two methods through the params constructor.</summary>
    [AcceptVerbs("GET", "POST", Route = "multi")]
    public IActionResult Multi() => Ok();

    /// <summary>One method through the string constructor.</summary>
    [AcceptVerbs("PUT", Route = "single")]
    public IActionResult Single() => Ok();

    /// <summary>[AcceptVerbs] and [Http*] with different methods: both are emitted.</summary>
    [AcceptVerbs("GET", Route = "union")]
    [HttpPost("union")]
    public IActionResult Union() => Ok();

    /// <summary>[AcceptVerbs] and [Http*] with the same method and route: emitted once.</summary>
    [AcceptVerbs("GET", Route = "same")]
    [HttpGet("same")]
    public IActionResult Same() => Ok();

    /// <summary>TRACE has its own field in every version.</summary>
    [AcceptVerbs("TRACE", Route = "trace")]
    public IActionResult Trace() => Ok();

    /// <summary>An explicit operationId shared by both operations of the action.</summary>
    [AcceptVerbs("GET", "POST", Route = "operation-id/explicit")]
    [Swashbuckle.AspNetCore.Annotations.SwaggerOperation(OperationId = "SharedExplicitId")]
    public IActionResult ExplicitOperationId() => Ok();

    /// <summary>No operationId source: none is synthesized.</summary>
    [AcceptVerbs("GET", "POST", Route = "operation-id/none")]
    public IActionResult NoOperationId() => Ok();

    /// <summary>The attribute's Name becomes the operationId.</summary>
    [AcceptVerbs("GET", Route = "operation-id/named", Name = "NamedByAttribute")]
    public IActionResult NamedOperationId() => Ok();

    /// <summary>Overload taking an integer; wins over the string overload by parameter type name.</summary>
    [HttpGet("overloads")]
    public IActionResult Overloaded([FromQuery] int value) => Ok();

    /// <summary>Overload taking a string.</summary>
    [HttpGet("overloads")]
    public IActionResult Overloaded([FromQuery] string value) => Ok();

    /// <summary>No method at all: not emitted, with a warning.</summary>
    [AcceptVerbs(Route = "empty")]
    public IActionResult Empty() => Ok();

    /// <summary>A custom method attribute: not emitted, with a warning.</summary>
    [HttpQuery("custom-query")]
    public IActionResult CustomQuery() => Ok();
}
