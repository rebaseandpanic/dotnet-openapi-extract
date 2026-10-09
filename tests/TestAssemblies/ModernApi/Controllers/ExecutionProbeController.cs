using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Probes;

namespace ModernApi.Controllers;

/// <summary>Makes the execution probes reachable from an action.</summary>
[ApiController]
[Route("execution-probe")]
public class ExecutionProbeController : ControllerBase
{
    /// <summary>Returns a probe DTO.</summary>
    [HttpGet]
    public ActionResult<ExecutionProbeDto> Get() => throw new NotSupportedException();
}
