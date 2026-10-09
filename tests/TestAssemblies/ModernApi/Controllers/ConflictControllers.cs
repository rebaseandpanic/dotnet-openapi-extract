using Microsoft.AspNetCore.Mvc;

namespace ModernApi.Controllers;

// Path+method conflicts between controllers. Types are discovered in declaration order, so the
// order below is deliberate: for "first-wins" the key winner (Alpha < Beta) is declared first,
// for "last-wins" the key winner (Yankee < Zulu) is declared last.

/// <summary>Key winner of the "first-wins" and "excluded" conflicts; declared first.</summary>
[ApiController]
[Route("conflicts")]
public class ConflictAlphaController : ControllerBase
{
    /// <summary>Alpha side of the first-wins conflict.</summary>
    [HttpGet("first-wins")]
    public IActionResult FirstWins() => Ok();

    /// <summary>Alpha side of a conflict on an excluded path.</summary>
    [HttpGet("excluded/clash")]
    public IActionResult ExcludedClash() => Ok();
}

/// <summary>Loser of the "first-wins" and "excluded" conflicts; declared second.</summary>
[ApiController]
[Route("conflicts")]
public class ConflictBetaController : ControllerBase
{
    /// <summary>Beta side of the first-wins conflict.</summary>
    [AcceptVerbs("GET", Route = "first-wins")]
    public IActionResult FirstWins() => Ok();

    /// <summary>Beta side of a conflict on an excluded path.</summary>
    [AcceptVerbs("GET", Route = "excluded/clash")]
    public IActionResult ExcludedClash() => Ok();
}

/// <summary>Loser of the "last-wins" conflict; declared first.</summary>
[ApiController]
[Route("conflicts")]
public class ConflictZuluController : ControllerBase
{
    /// <summary>Zulu side of the last-wins conflict.</summary>
    [HttpGet("last-wins")]
    public IActionResult LastWins() => Ok();
}

[ApiController]
[Route("conflicts")]
public class ConflictYankeeController : ControllerBase
{
    // Key winner of the "last-wins" conflict, declared last. No summary on purpose: validation
    // reports it, and the report must point at this controller.
    [HttpGet("last-wins")]
    public IActionResult LastWins() => Ok();
}
