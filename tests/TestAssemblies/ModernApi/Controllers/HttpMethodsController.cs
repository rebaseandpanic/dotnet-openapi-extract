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

    /// <summary>No method at all: not emitted, with a warning.</summary>
    [AcceptVerbs(Route = "empty")]
    public IActionResult Empty() => Ok();

    /// <summary>A custom method attribute: not emitted, with a warning.</summary>
    [HttpQuery("custom-query")]
    public IActionResult CustomQuery() => Ok();
}
