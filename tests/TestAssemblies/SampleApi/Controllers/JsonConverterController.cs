using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Controller for testing [JsonConverter] schema generation.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[SwaggerTag("JsonConverter schema tests")]
public class JsonConverterController : ControllerBase
{
    /// <summary>
    /// Get a JsonConverterTestDto — used to exercise property-level [JsonConverter] schema generation.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(JsonConverterTestDto), StatusCodes.Status200OK)]
    [SwaggerOperation(Summary = "Get converter test DTO", OperationId = "GetConverterTestDto")]
    public ActionResult<JsonConverterTestDto> Get()
    {
        throw new NotImplementedException();
    }
}
