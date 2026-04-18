using Microsoft.AspNetCore.Mvc;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Exposes <see cref="ObsoleteDto"/> so its schema ends up in Components.Schemas.
/// </summary>
[ApiController]
[Route("api/obsolete-dto")]
public class ObsoleteDtoController : ControllerBase
{
    /// <summary>Returns an instance of the obsolete DTO.</summary>
    [HttpGet]
#pragma warning disable CS0618 // ObsoleteDto is intentionally used here to verify schema generation
    public ObsoleteDto Get() => new();
#pragma warning restore CS0618
}
