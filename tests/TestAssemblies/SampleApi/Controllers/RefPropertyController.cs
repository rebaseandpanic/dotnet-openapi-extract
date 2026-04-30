using Microsoft.AspNetCore.Mvc;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Controller that surfaces OuterWithRefPropertyModel so its schema (including
/// $ref-typed properties with sibling keywords) appears in Components.Schemas.
/// </summary>
[ApiController]
[Route("api/v1/ref-property")]
public class RefPropertyController : ControllerBase
{
    /// <summary>Get the outer model.</summary>
    /// <response code="200">Returns the model.</response>
    [HttpGet]
    [ProducesResponseType(typeof(OuterWithRefPropertyModel), StatusCodes.Status200OK)]
    public ActionResult<OuterWithRefPropertyModel> Get()
    {
        throw new NotImplementedException();
    }
}
