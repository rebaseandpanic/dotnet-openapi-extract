using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace SampleApi.Controllers;

/// <summary>
/// Controller that references framework types (e.g. ProblemDetails) via ProducesResponseType,
/// used to verify that XML docs for framework types are loaded from SDK ref packs.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[SwaggerTag("Framework type references")]
public class FrameworkTypeRefController : ControllerBase
{
    /// <summary>
    /// Returns a resource or a problem details object on error.
    /// </summary>
    /// <remarks>
    /// This endpoint explicitly declares ProblemDetails as a response type so the extractor
    /// includes Microsoft.AspNetCore.Mvc.ProblemDetails in components/schemas.
    /// The test verifies that the schema description comes from SDK ref-pack XML docs.
    /// </remarks>
    /// <response code="200">Resource found</response>
    /// <response code="422">Validation error with ProblemDetails body</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [SwaggerOperation(
        Summary = "Get resource with ProblemDetails error",
        Description = "Returns a resource or a ProblemDetails error body. Used to test framework XML doc loading.",
        OperationId = "GetResourceWithProblemDetails",
        Tags = new[] { "Framework type references" })]
    public ActionResult<string> GetResource(
        [FromRoute, SwaggerParameter("Resource identifier")] int id)
    {
        if (id <= 0)
            return UnprocessableEntity(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Title = "Invalid id",
                Status = 422,
            });
        return Ok("resource");
    }
}
