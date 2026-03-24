using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Not a controller — should NOT be discovered
/// </summary>
[NonController]
public class NonApiController : ControllerBase
{
    [HttpGet("test")]
    public ActionResult Test() => Ok();
}
