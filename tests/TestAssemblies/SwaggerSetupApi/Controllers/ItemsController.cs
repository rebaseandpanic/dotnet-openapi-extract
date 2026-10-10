using Microsoft.AspNetCore.Mvc;

namespace SwaggerSetupApi.Controllers;

/// <summary>Items.</summary>
[ApiController]
[Route("items")]
public class ItemsController : ControllerBase
{
    /// <summary>Lists the items.</summary>
    [HttpGet]
    public ActionResult<string[]> List() => Ok(Array.Empty<string>());
}
