using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Abstract base — should NOT be discovered
/// </summary>
[ApiController]
[Route("api/base")]
public abstract class AbstractBaseController : ControllerBase
{
    [HttpGet("info")]
    public ActionResult GetInfo() => Ok();
}
