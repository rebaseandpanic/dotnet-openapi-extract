using Microsoft.AspNetCore.Mvc;
using ModernApi.Models;

namespace ModernApi.Controllers;

/// <summary>
/// The same body on GET, HEAD and DELETE, which OpenAPI 3.0 consumers must ignore, and on POST,
/// where it is ordinary.
/// </summary>
[ApiController]
[Route("safe-method-body")]
public class SafeMethodBodyController : ControllerBase
{
    /// <summary>Search with a body on GET.</summary>
    [HttpGet("search")]
    public IActionResult SearchGet([FromBody] SearchFilter filter) => Ok();

    /// <summary>Search with a body on HEAD.</summary>
    [HttpHead("search")]
    public IActionResult SearchHead([FromBody] SearchFilter filter) => Ok();

    /// <summary>Delete matches of a body filter.</summary>
    [HttpDelete("search")]
    public IActionResult SearchDelete([FromBody] SearchFilter filter) => Ok();

    /// <summary>Search with a body on POST.</summary>
    [HttpPost("search")]
    public IActionResult SearchPost([FromBody] SearchFilter filter) => Ok();
}

/// <summary>A GET with a body under a path prefix that tests exclude.</summary>
[ApiController]
[Route("excluded/safe-method-body")]
public class ExcludedSafeMethodBodyController : ControllerBase
{
    /// <summary>Search with a body on GET, under an excluded prefix.</summary>
    [HttpGet("search")]
    public IActionResult SearchGet([FromBody] SearchFilter filter) => Ok();
}
