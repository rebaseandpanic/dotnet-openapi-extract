using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace VersionedApi.Controllers;

/// <summary>An agent.</summary>
/// <param name="Id">Identifier of the agent.</param>
public sealed record Agent(string Id);

/// <summary>Agents, version 1.0, versioned by URL segment.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/agents")]
public sealed class AgentsController : ControllerBase
{
    /// <summary>Lists the agents.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Agent[]), StatusCodes.Status200OK)]
    public IActionResult List() => Ok(Array.Empty<Agent>());

    /// <summary>Gets one agent.</summary>
    /// <param name="id">Identifier of the agent.</param>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Agent), StatusCodes.Status200OK)]
    public IActionResult Get(string id) => Ok(new Agent(id));
}

/// <summary>Health of the service, with an API version route parameter but no declared version.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>Reports that the service runs.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok();
}
