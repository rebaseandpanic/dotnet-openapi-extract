using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ModernApi.Controllers;

/// <summary>Roles of [Authorize] combined with the effective security schemes.</summary>
[ApiController]
[Route("roles")]
public class RoleRequirementsController : ControllerBase
{
    /// <summary>Roles of one attribute are alternatives.</summary>
    [HttpGet("or")]
    [Authorize(Roles = "A,B", AuthenticationSchemes = "key")]
    public IActionResult Or() => Ok();

    /// <summary>Roles without schemes use the document's requirements.</summary>
    [HttpGet("inherited")]
    [Authorize(Roles = "A")]
    public IActionResult Inherited() => Ok();

    /// <summary>Roles next to an OAuth2 scheme.</summary>
    [HttpGet("oauth")]
    [Authorize(Roles = "A", AuthenticationSchemes = "oauth,key")]
    public IActionResult OAuth() => Ok();

    /// <summary>Three attributes of three roles each.</summary>
    [HttpGet("large")]
    [Authorize(Roles = "A1,A2,A3")]
    [Authorize(Roles = "B1,B2,B3")]
    [Authorize(Roles = "C1,C2,C3")]
    public IActionResult Large() => Ok();

    /// <summary>Roles with anonymous access.</summary>
    [HttpGet("anonymous")]
    [Authorize(Roles = "A")]
    [AllowAnonymous]
    public IActionResult Anonymous() => Ok();

    /// <summary>Roles on a QUERY operation.</summary>
    [AcceptVerbs("QUERY", Route = "query")]
    [Authorize(Roles = "A", AuthenticationSchemes = "key")]
    public IActionResult Query() => Ok();
}

/// <summary>Roles on the controller and on the action must all hold.</summary>
[ApiController]
[Route("controller-roles")]
[Authorize(Roles = "A")]
public class ControllerRolesController : ControllerBase
{
    /// <summary>Controller roles AND action roles.</summary>
    [HttpGet("and")]
    [Authorize(Roles = "C,D", AuthenticationSchemes = "key")]
    public IActionResult And() => Ok();

    /// <summary>Only the controller's roles.</summary>
    [HttpGet("plain")]
    public IActionResult Plain() => Ok();
}

/// <summary>Several authentication schemes: ASP.NET Core accepts a request any one of them authenticates.</summary>
[ApiController]
[Route("scheme-alternatives")]
public class SchemeAlternativesController : ControllerBase
{
    /// <summary>Two schemes of one attribute.</summary>
    [HttpGet("either")]
    [Authorize(AuthenticationSchemes = "key,bearer")]
    public IActionResult Either() => Ok();
}

/// <summary>A scheme on the controller and another on the action: one policy of both schemes.</summary>
[ApiController]
[Route("scheme-alternatives-combined")]
[Authorize(AuthenticationSchemes = "key")]
public class CombinedSchemesController : ControllerBase
{
    /// <summary>The action's scheme and the controller's.</summary>
    [HttpGet]
    [Authorize(AuthenticationSchemes = "bearer")]
    public IActionResult Get() => Ok();
}
