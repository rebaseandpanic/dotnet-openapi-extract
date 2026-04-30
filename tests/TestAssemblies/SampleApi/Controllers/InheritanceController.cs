using Microsoft.AspNetCore.Mvc;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Controller that surfaces inheritance fixtures so their component schemas are emitted.
/// Used to verify that inherited property descriptions propagate from the base type's
/// XML doc key to the derived type's component schema.
/// </summary>
[ApiController]
[Route("api/v1/inheritance")]
public class InheritanceController : ControllerBase
{
    /// <summary>Create a server using the derived request type.</summary>
    /// <response code="200">Server created.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CreateServerRequest), StatusCodes.Status200OK)]
    public ActionResult<CreateServerRequest> CreateServer([FromBody] CreateServerRequest request)
    {
        throw new NotImplementedException();
    }
}
