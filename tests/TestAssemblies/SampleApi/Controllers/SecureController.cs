using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Endpoints that demonstrate [Authorize] and [AllowAnonymous] behavior.
/// </summary>
[ApiController]
[Route("api/secure")]
[Authorize]
public class SecureController : ControllerBase
{
    /// <summary>
    /// Requires authorization (inherited from controller).
    /// </summary>
    [HttpGet]
    public IActionResult Get() => Ok();

    /// <summary>
    /// Publicly accessible endpoint — [AllowAnonymous] overrides [Authorize] on controller.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult GetPublic() => Ok();

    /// <summary>
    /// Requires the Admin policy with Bearer scheme.
    /// </summary>
    [HttpGet("admin")]
    [Authorize(Policy = "Admin", AuthenticationSchemes = "Bearer")]
    public IActionResult GetAdmin() => Ok();
}
