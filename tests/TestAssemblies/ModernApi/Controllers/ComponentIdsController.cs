using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Catalog;

namespace ModernApi.Controllers;

/// <summary>Responses whose types share a short name with another type.</summary>
[ApiController]
[Route("component-ids")]
public class ComponentIdsController : ControllerBase
{
    /// <summary>Alpha page of catalog items.</summary>
    [HttpGet("alpha-page")]
    public ActionResult<Models.Alpha.Page<Item>> AlphaPage() => new Models.Alpha.Page<Item>();

    /// <summary>Beta page of catalog items.</summary>
    [HttpGet("beta-page")]
    public ActionResult<Models.Beta.Page<Item>> BetaPage() => new Models.Beta.Page<Item>();

    /// <summary>Alpha summary.</summary>
    [HttpGet("alpha-summary")]
    public ActionResult<Models.Alpha.Summary> AlphaSummary() => new Models.Alpha.Summary();

    /// <summary>Beta summary.</summary>
    [HttpGet("beta-summary")]
    public ActionResult<Models.Beta.Summary> BetaSummary() => new Models.Beta.Summary();
}
