using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Controller fixture for rate-limiting attribute extraction tests.
/// Demonstrates [EnableRateLimiting] on the controller and individual action overrides.
/// </summary>
[ApiController]
[Route("api/rl")]
[EnableRateLimiting("default-policy")]
public class RateLimitingController : ControllerBase
{
    /// <summary>
    /// Inherits the controller-level [EnableRateLimiting("default-policy")].
    /// </summary>
    [HttpGet]
    public IActionResult Get() => Ok();

    /// <summary>
    /// Overrides the controller-level policy with action-level [EnableRateLimiting("strict")].
    /// </summary>
    [HttpGet("override")]
    [EnableRateLimiting("strict")]
    public IActionResult GetOverride() => Ok();

    /// <summary>
    /// Explicitly disables rate limiting with [DisableRateLimiting].
    /// </summary>
    [HttpGet("disabled")]
    [DisableRateLimiting]
    public IActionResult GetDisabled() => Ok();

    /// <summary>
    /// Both [DisableRateLimiting] and [EnableRateLimiting] present on the same action.
    /// [DisableRateLimiting] must win unconditionally.
    /// </summary>
    [HttpGet("disable-wins")]
    [DisableRateLimiting]
    [EnableRateLimiting("strict")]
    public IActionResult GetDisableWins() => Ok();
}
