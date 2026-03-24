using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Internal controller that should not appear in OpenAPI
/// </summary>
[ApiController]
[Route("api/internal")]
[ApiExplorerSettings(IgnoreApi = true)]
public class InternalController : ControllerBase
{
    [HttpGet("debug")]
    public ActionResult Debug() => Ok();
}
