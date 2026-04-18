using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace SampleApi.Controllers;

/// <summary>
/// Controller fixture for response caching attribute extraction tests.
/// Demonstrates [ResponseCache] and [OutputCache] attributes on individual actions.
/// Controller-level [ResponseCache(Duration = 999)] is intentionally different from
/// action-level attributes to exercise the override precedence logic.
/// </summary>
[ApiController]
[Route("api/cache")]
[ResponseCache(Duration = 999)]
public class CachingController : ControllerBase
{
    /// <summary>
    /// Cached for 60 seconds, location Any, vary by Accept-Encoding.
    /// [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept-Encoding")]
    /// </summary>
    [HttpGet("response-cache")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept-Encoding")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetCached() => Ok();

    /// <summary>
    /// Caching disabled via NoStore.
    /// [ResponseCache(NoStore = true)]
    /// </summary>
    [HttpGet("no-store")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetNoStore() => Ok();

    /// <summary>
    /// Cached via OutputCache for 30 seconds.
    /// [OutputCache(Duration = 30)]
    /// </summary>
    [HttpGet("output-cache")]
    [OutputCache(Duration = 30)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetOutputCached() => Ok();

    /// <summary>
    /// Cached for 30 seconds, location Client (browser-only, private cache).
    /// [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
    /// </summary>
    [HttpGet("client-cache")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetClientCached() => Ok();

    /// <summary>
    /// NoStore=true combined with Duration=30: both directives must be emitted.
    /// [ResponseCache(NoStore = true, Duration = 30)]
    /// </summary>
    [HttpGet("no-store-with-duration")]
    [ResponseCache(NoStore = true, Duration = 30)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetNoStoreWithDuration() => Ok();

    /// <summary>
    /// Location=None: must emit no-cache directive.
    /// [ResponseCache(Duration = 60, Location = ResponseCacheLocation.None)]
    /// </summary>
    [HttpGet("location-none")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetLocationNone() => Ok();
}
