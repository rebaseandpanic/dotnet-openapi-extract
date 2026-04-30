using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Controller that declares its request/response DTOs as nested types.
/// Used to verify that XML doc descriptions are resolved for nested types,
/// where reflection emits 'Outer+Inner' but the XML compiler emits 'Outer.Inner'.
/// </summary>
[ApiController]
[Route("api/v1/nested-dto")]
public class NestedDtoController : ControllerBase
{
    /// <summary>DTO declared as a nested type inside this controller.</summary>
    public class ServiceDto
    {
        /// <summary>Service name</summary>
        public required string Name { get; set; }

        /// <summary>Service endpoint URL</summary>
        public string? Endpoint { get; set; }

        /// <summary>Endpoint listing for the service</summary>
        public ServiceEndpoints? Endpoints { get; set; }

        /// <summary>Endpoints info, nested two levels deep inside the controller</summary>
        public class ServiceEndpoints
        {
            /// <summary>Health probe path</summary>
            public string? HealthPath { get; set; }
        }
    }

    /// <summary>Response wrapper declared as a nested type inside this controller.</summary>
    public class ServiceResponse
    {
        /// <summary>Whether the service call succeeded</summary>
        public bool Success { get; set; }

        /// <summary>The service data payload</summary>
        public ServiceDto? Data { get; set; }
    }

    /// <summary>Get a service descriptor.</summary>
    /// <response code="200">Service descriptor returned.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ServiceResponse), StatusCodes.Status200OK)]
    public ActionResult<ServiceResponse> GetService([FromRoute] string id)
    {
        throw new NotImplementedException();
    }

    /// <summary>Create a service descriptor.</summary>
    /// <response code="200">Service descriptor created.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ServiceResponse), StatusCodes.Status200OK)]
    public ActionResult<ServiceResponse> CreateService([FromBody] ServiceDto request)
    {
        throw new NotImplementedException();
    }
}
