using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Keywords;

namespace ModernApi.Controllers;

/// <summary>Parameters, bodies and form fields that reference a component and carry keywords of their own.</summary>
[ApiController]
[Route("ref-siblings-ops")]
public class RefSiblingsOperationsController : ControllerBase
{
    /// <summary>Query parameters: one class of keyword each, allowed values, a default, a nullable reference.</summary>
    [HttpGet("query")]
    public IActionResult Query(
        [FromQuery, MinLength(1), MaxLength(5)] SiblingTarget lengths,
        [FromQuery, RegularExpression("^place$")] SiblingTarget patterned,
        [FromQuery, EmailAddress] SiblingTarget formatted,
        [FromQuery, AllowedValues("low", "high"), DeniedValues("mid")] SiblingTarget allowed,
        [FromQuery, AllowedValues("low"), DefaultValue("low")] SiblingTarget defaulted,
        [FromQuery, AllowedValues("low", "high"), DeniedValues("mid")] SiblingPoint? nullableAllowed) => Ok();

    /// <summary>A path parameter with a keyword.</summary>
    [HttpGet("path/{target}")]
    public IActionResult Path([FromRoute, MaxLength(5)] SiblingTarget target) => Ok();

    /// <summary>A header parameter with a keyword.</summary>
    [HttpGet("header")]
    public IActionResult Header([FromHeader(Name = "X-Target"), RegularExpression("^place$")] SiblingTarget target) => Ok();

    /// <summary>A body with lengths.</summary>
    [HttpPost("body")]
    public IActionResult Body([FromBody, MinLength(1), MaxLength(5)] SiblingTarget body) => Ok();

    /// <summary>A body with one allowed value and a denied one.</summary>
    [HttpPost("body-allowed")]
    public IActionResult BodyAllowed([FromBody, AllowedValues("low"), DeniedValues("mid")] SiblingTarget body) => Ok();

    /// <summary>A nullable body with allowed and denied values.</summary>
    [HttpPost("body-nullable")]
    public IActionResult BodyNullable([FromBody, AllowedValues("low", "high"), DeniedValues("mid")] SiblingPoint? body) => Ok();

    /// <summary>Form fields: a keyword, allowed values, a nullable reference.</summary>
    [HttpPost("form")]
    public IActionResult Form(
        [FromForm, RegularExpression("^place$"), MaxLength(5)] SiblingTarget field,
        [FromForm, AllowedValues("low")] SiblingTarget allowedField,
        [FromForm, AllowedValues("low", "high"), DeniedValues("mid")] SiblingPoint? nullableField) => Ok();
}
