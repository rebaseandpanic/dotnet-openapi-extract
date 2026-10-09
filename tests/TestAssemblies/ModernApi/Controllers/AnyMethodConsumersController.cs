using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models;

namespace ModernApi.Controllers;

/// <summary>
/// Pairs of operations declared identically except for the HTTP method: QUERY next to GET, LINK
/// next to POST. Every document-wide mechanism must treat both alike.
/// </summary>
[ApiController]
[Route("consumers")]
public class AnyMethodConsumersController : ControllerBase
{
    /// <summary>Search the catalog.</summary>
    [AcceptVerbs("QUERY", Route = "search")]
    [Authorize(AuthenticationSchemes = "Undeclared")]
    public ActionResult<SearchFilter> Query([FromBody] SearchFilter filter) => filter;

    /// <summary>Search the catalog.</summary>
    [HttpGet("search")]
    [Authorize(AuthenticationSchemes = "Undeclared")]
    public ActionResult<SearchFilter> Get([FromBody] SearchFilter filter) => filter;

    /// <summary>Link two catalog entries.</summary>
    [AcceptVerbs("LINK", Route = "link")]
    [Authorize(AuthenticationSchemes = "Undeclared")]
    public ActionResult<SearchFilter> Link([FromBody] SearchFilter filter) => filter;

    /// <summary>Link two catalog entries.</summary>
    [HttpPost("link")]
    [Authorize(AuthenticationSchemes = "Undeclared")]
    public ActionResult<SearchFilter> Post([FromBody] SearchFilter filter) => filter;
}
