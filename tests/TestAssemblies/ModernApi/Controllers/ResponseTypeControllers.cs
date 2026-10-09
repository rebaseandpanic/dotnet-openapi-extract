using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Responses;
using Swashbuckle.AspNetCore.Annotations;

namespace ModernApi.Controllers;

/// <summary>Response type sources on actions only.</summary>
[ApiController]
[Route("response-types")]
public class ResponseTypeSourcesController : ControllerBase
{
    /// <summary>Type for the code wins over [Produces] and the signature.</summary>
    [HttpGet("declared-over-produces")]
    [ProducesResponseType(typeof(DeclaredBody), StatusCodes.Status200OK)]
    [Produces(typeof(ProducedBody))]
    public ActionResult<SignatureBody> DeclaredOverProduces() => new SignatureBody();

    /// <summary>[SwaggerResponse] type for the code wins over [Produces].</summary>
    [HttpGet("swagger-response-over-produces")]
    [SwaggerResponse(StatusCodes.Status200OK, "Swagger described", typeof(DeclaredBody))]
    [Produces(typeof(ProducedBody))]
    public ActionResult<SignatureBody> SwaggerResponseOverProduces() => new SignatureBody();

    /// <summary>Generic response attribute.</summary>
    [HttpGet("generic-declared")]
    [ProducesResponseType<DeclaredBody>(StatusCodes.Status200OK)]
    public ActionResult<SignatureBody> GenericDeclared() => new SignatureBody();

    /// <summary>[Produces(typeof(T))] wins over the signature.</summary>
    [HttpGet("produces-over-signature")]
    [Produces(typeof(ProducedBody))]
    public ActionResult<SignatureBody> ProducesOverSignature() => new SignatureBody();

    /// <summary>[Produces&lt;T&gt;] gives a body to an untyped result.</summary>
    [HttpGet("generic-produces")]
    [Produces<ProducedBody>]
    public IActionResult GenericProduces() => Ok();

    /// <summary>A 200 declared without a type takes the [Produces] type.</summary>
    [HttpGet("untyped-declared-produces")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces(typeof(ProducedBody))]
    public ActionResult<SignatureBody> UntypedDeclaredProduces() => new SignatureBody();

    /// <summary>A 200 declared without a type takes the signature type.</summary>
    [HttpGet("untyped-declared-signature")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SignatureBody> UntypedDeclaredSignature() => new SignatureBody();

    /// <summary>No source but the signature.</summary>
    [HttpGet("signature-only")]
    public async Task<ActionResult<SignatureBody>> SignatureOnly() => await Task.FromResult(new SignatureBody());

    /// <summary>[Produces(typeof(T))] declares the 200 response next to other declared codes.</summary>
    [HttpGet("produces-with-error")]
    [Produces(typeof(ProducedBody))]
    [ProducesResponseType(typeof(ActionError), StatusCodes.Status404NotFound)]
    public IActionResult ProducesWithError() => Ok();
}

/// <summary>Response type sources on the controller and its actions.</summary>
/// <response code="404">Controller says not found.</response>
/// <response code="409">Controller says conflict.</response>
[ApiController]
[Route("controller-responses")]
[Produces(typeof(ControllerProducedBody))]
[ProducesResponseType(typeof(ControllerError), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ControllerError), StatusCodes.Status409Conflict)]
public class ControllerResponsesController : ControllerBase
{
    /// <summary>Only controller declarations.</summary>
    [HttpGet("inherits")]
    public ActionResult<SignatureBody> Inherits() => new SignatureBody();

    /// <summary>The action's own [Produces] type wins over the controller's.</summary>
    [HttpGet("own-produces")]
    [Produces(typeof(ProducedBody))]
    public ActionResult<SignatureBody> OwnProduces() => new SignatureBody();

    /// <summary>The action's own declaration for 404 wins over the controller's.</summary>
    /// <response code="404">Action says not found.</response>
    [HttpGet("own-not-found")]
    [ProducesResponseType(typeof(ActionError), StatusCodes.Status404NotFound)]
    public ActionResult<SignatureBody> OwnNotFound() => new SignatureBody();

    /// <summary>The action declares 409 without a type: the controller fills the type.</summary>
    [HttpGet("untyped-conflict")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public ActionResult<SignatureBody> UntypedConflict() => new SignatureBody();
}

/// <summary>A controller-level type for the code wins over an action's [Produces].</summary>
[ApiController]
[Route("controller-declared")]
[ProducesResponseType(typeof(ControllerDeclaredBody), StatusCodes.Status200OK)]
public class ControllerDeclaredController : ControllerBase
{
    /// <summary>Controller type for 200 over the action's [Produces].</summary>
    [HttpGet("over-action-produces")]
    [Produces(typeof(ProducedBody))]
    public ActionResult<SignatureBody> OverActionProduces() => new SignatureBody();

    /// <summary>The action's own type for 200 over the controller's.</summary>
    [HttpGet("action-declared")]
    [ProducesResponseType(typeof(DeclaredBody), StatusCodes.Status200OK)]
    public ActionResult<SignatureBody> ActionDeclared() => new SignatureBody();
}

/// <summary>A controller that declares only an error response.</summary>
[ApiController]
[Route("controller-error-only")]
[ProducesResponseType(typeof(ControllerError), StatusCodes.Status404NotFound)]
public class ControllerErrorOnlyController : ControllerBase
{
    /// <summary>The return type adds no 200: a response is declared (as in ApiExplorer).</summary>
    [HttpGet("typed")]
    public ActionResult<SignatureBody> Typed() => new SignatureBody();
}
