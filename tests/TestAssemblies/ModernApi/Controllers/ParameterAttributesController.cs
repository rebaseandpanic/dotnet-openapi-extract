using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Keywords;
using Swashbuckle.AspNetCore.Annotations;

namespace ModernApi.Controllers;

/// <summary>Validation attributes and Swashbuckle declarations on action parameters.</summary>
[ApiController]
[Route("parameter-attributes")]
[SwaggerTag("Parameter attributes", "https://example.com/docs/parameters")]
public class ParameterAttributesController : ControllerBase
{
    /// <summary>Constraints on parameters.</summary>
    [HttpGet("constraints/{code}")]
    public IActionResult Constraints(
        [StringLength(8, MinimumLength = 2)] string code,
        [FromQuery, Range(1, 10, MinimumIsExclusive = true)] int level,
        [FromQuery, Range(0.5, 9.5, MaximumIsExclusive = true)] double ratio,
        [FromQuery, RegularExpression("^[a-z]+$")] string? slug,
        [FromQuery, MinLength(1), MaxLength(3)] string[]? tags,
        [FromQuery, Length(2, 5)] string? word,
        [FromQuery, Length(1, 4)] int[]? ids,
        [FromQuery, EmailAddress] string? mail,
        [FromQuery, AllowedValues("red", "green")] string? color,
        [FromQuery, DeniedValues(0)] int? count,
        [FromQuery, Length(1, 10), MaxLength(4)] string? clipped,
        [FromQuery, Length(3, 9), MinLength(5)] int[]? pick) => Ok();

    /// <summary>Declared requiredness.</summary>
    [HttpGet("required/{id}")]
    public IActionResult Required(
        [SwaggerParameter("Path id", Required = false)] int id,
        [FromQuery, SwaggerParameter(Required = true)] string? optionalByType,
        [FromQuery, SwaggerParameter(Required = false)] int requiredByType,
        [FromQuery] int inferred) => Ok();

    /// <summary>Constraints on a scalar body.</summary>
    [HttpPost("body-scalar")]
    public IActionResult BodyScalar([FromBody, StringLength(20, MinimumLength = 3)] string text) => Ok();

    /// <summary>Constraints on a collection body.</summary>
    [HttpPost("body-collection")]
    public IActionResult BodyCollection([FromBody, MinLength(1), MaxLength(5)] List<int> items) => Ok();

    /// <summary>Allowed values of an enum body with renamed members.</summary>
    [HttpPost("body-enum")]
    public IActionResult BodyEnum([FromBody, AllowedValues(StjTint.Red, StjTint.Crimson)] StjTint tint) => Ok();

    /// <summary>A declared format on a reference body.</summary>
    [HttpPost("body-reference")]
    public IActionResult BodyReference([FromBody, SwaggerSchema(Format = "x-doc")] AnnotatedTarget target) => Ok();

    /// <summary>Constraints and requiredness of form fields.</summary>
    [HttpPost("form-constraints")]
    public IActionResult FormConstraints(
        [FromForm, StringLength(10, MinimumLength = 2)] string title,
        [FromForm, Range(1, 5)] int pages,
        [FromForm, MaxLength(3)] string[]? tags,
        [FromForm, SwaggerParameter(Required = false)] string note,
        [FromForm] int? optional) => Ok();

    /// <summary>A form field whose constraint and example cannot be written.</summary>
    /// <param name="count" example="many">A count.</param>
    [HttpPost("form-unwritable")]
    public IActionResult FormUnwritable([FromForm, AllowedValues("many")] int count) => Ok();
}
