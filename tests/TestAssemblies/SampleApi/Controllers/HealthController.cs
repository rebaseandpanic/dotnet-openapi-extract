using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace SampleApi.Controllers;

/// <summary>
/// Health check endpoints
/// </summary>
[ApiController]
[Route("[controller]")]
[SwaggerTag("Health checks")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Liveness probe
    /// </summary>
    /// <response code="200">Service is alive</response>
    [HttpGet("/healthz")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Liveness probe", OperationId = "Healthz")]
    public ActionResult Healthz() => Ok();
}
