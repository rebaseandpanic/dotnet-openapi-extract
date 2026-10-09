using Microsoft.AspNetCore.Mvc;

namespace ModernApi.Controllers;

/// <summary>Operations whose methods have no Path Item field before OpenAPI 3.2.</summary>
[ApiController]
[Route("additional-methods")]
public class AdditionalMethodsController : ControllerBase
{
    /// <summary>
    /// QUERY. HttpMethods.Query is a static readonly field, not a constant, so attributes can only
    /// take the literal.
    /// </summary>
    [AcceptVerbs("QUERY", Route = "search")]
    public IActionResult Search() => Ok();

    /// <summary>QUERY on a second path.</summary>
    [AcceptVerbs("QUERY", Route = "search-two")]
    public IActionResult SearchTwo() => Ok();

    /// <summary>A non-standard method in upper case.</summary>
    [AcceptVerbs("LINK", Route = "link")]
    public IActionResult Link() => Ok();

    /// <summary>A non-standard method in mixed case: the key keeps this capitalization.</summary>
    [AcceptVerbs("Purge", Route = "purge")]
    public IActionResult Purge() => Ok();

    /// <summary>First of two QUERY actions on one path (the key winner: ClashA &lt; ClashB).</summary>
    [AcceptVerbs("QUERY", Route = "clash")]
    public IActionResult ClashA() => Ok();

    /// <summary>Second of two QUERY actions on one path.</summary>
    [AcceptVerbs("QUERY", Route = "clash")]
    public IActionResult ClashB() => Ok();
}

/// <summary>A QUERY operation under a path prefix that tests exclude.</summary>
[ApiController]
[Route("excluded-additional")]
public class ExcludedAdditionalMethodsController : ControllerBase
{
    /// <summary>QUERY under an excluded prefix.</summary>
    [AcceptVerbs("QUERY", Route = "search")]
    public IActionResult Search() => Ok();
}
