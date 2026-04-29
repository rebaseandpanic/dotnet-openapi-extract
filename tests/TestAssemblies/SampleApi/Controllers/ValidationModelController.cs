using Microsoft.AspNetCore.Mvc;
using SampleApi.Models;

namespace SampleApi.Controllers;

/// <summary>
/// Controller exists solely to surface ValidationModel / ExtendedValidationModel
/// in the generated OpenAPI spec so that schema-level validation rules
/// (schema.property-constraints in particular) have something to run against.
/// </summary>
[ApiController]
[Route("api/v1/validation-models")]
public class ValidationModelController : ControllerBase
{
    /// <summary>Echo a ValidationModel so its schema is emitted.</summary>
    /// <param name="model">The validation model to echo.</param>
    /// <response code="200">Returned on success.</response>
    /// <response code="422">Business-error response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ValidationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<ValidationModel> Echo([FromBody] ValidationModel model)
    {
        throw new NotImplementedException();
    }

    /// <summary>Echo an ExtendedValidationModel so its schema is emitted.</summary>
    /// <param name="model">The extended validation model to echo.</param>
    /// <response code="200">Returned on success.</response>
    /// <response code="422">Business-error response.</response>
    [HttpPost("extended")]
    [ProducesResponseType(typeof(ExtendedValidationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<ExtendedValidationModel> EchoExtended([FromBody] ExtendedValidationModel model)
    {
        throw new NotImplementedException();
    }

    /// <summary>Surface positional-record fixtures so their component schemas are emitted.</summary>
    /// <param name="customer">A positional record with default-target validation attributes.</param>
    /// <response code="200">Returned on success.</response>
    /// <response code="422">Business-error response.</response>
    [HttpPost("positional-customer")]
    [ProducesResponseType(typeof(CreatePositionalCustomerRequest), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<CreatePositionalCustomerRequest> EchoPositionalCustomer(
        [FromBody] CreatePositionalCustomerRequest customer)
    {
        throw new NotImplementedException();
    }

    /// <summary>Surface SecondaryCtorRecord so the merge fix is validated end-to-end.</summary>
    /// <param name="model">A positional record with a secondary same-named constructor.</param>
    /// <response code="200">Returned on success.</response>
    [HttpPost("secondary-ctor-record")]
    [ProducesResponseType(typeof(SecondaryCtorRecord), StatusCodes.Status200OK)]
    public ActionResult<SecondaryCtorRecord> EchoSecondaryCtorRecord([FromBody] SecondaryCtorRecord model)
    {
        throw new NotImplementedException();
    }
}
