using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Controller marked obsolete at the class level — all its actions must be deprecated in the spec.
/// </summary>
[ApiController]
[Route("api/deprecated")]
[Obsolete("Use NewController instead")]
public class DeprecatedController : ControllerBase
{
    /// <summary>Get endpoint on a deprecated controller.</summary>
    [HttpGet]
    public IActionResult Get() => Ok();

    /// <summary>Post endpoint on a deprecated controller.</summary>
    [HttpPost]
    public IActionResult Post() => Ok();
}
