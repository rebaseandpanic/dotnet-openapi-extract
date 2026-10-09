using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Keywords;

namespace ModernApi.Controllers;

/// <summary>XML examples of parameters and request bodies.</summary>
[ApiController]
[Route("parameter-examples")]
public class ParameterExamplesController : ControllerBase
{
    /// <summary>Path, query and header examples.</summary>
    /// <param name="id" example="42">The id.</param>
    /// <param name="ratio" example="1.5">A ratio.</param>
    /// <param name="flag" example="true">A flag.</param>
    /// <param name="code" example="123">A code that looks like a number.</param>
    /// <param name="maybe" example="null">An optional number.</param>
    /// <param name="trace" example="abc-1">A renamed header.</param>
    [HttpGet("values/{id}")]
    public IActionResult Values(
        int id,
        [FromQuery] double ratio,
        [FromQuery] bool flag,
        [FromQuery] string code,
        [FromQuery] int? maybe,
        [FromHeader(Name = "X-Trace")] string trace) => Ok();

    /// <summary>Parameter examples that do not parse.</summary>
    /// <param name="count" example="many">Not a number.</param>
    /// <param name="mandatory" example="null">Null for a non-nullable number.</param>
    [HttpGet("broken")]
    public IActionResult Broken([FromQuery] int count, [FromQuery] int mandatory) => Ok();

    /// <summary>A body example.</summary>
    /// <param name="body" example='{"label": "from the body"}'>The body.</param>
    [HttpPost("body")]
    public IActionResult Body([FromBody] ExampleTarget body) => Ok();

    /// <summary>A body example that is not JSON.</summary>
    /// <param name="body" example="not json">The body.</param>
    [HttpPost("body-broken")]
    public IActionResult BodyBroken([FromBody] ExampleTarget body) => Ok();

    /// <summary>Null for a non-nullable body.</summary>
    /// <param name="body" example="null">The body.</param>
    [HttpPost("body-null")]
    public IActionResult BodyNull([FromBody] ExampleTarget body) => Ok();

    /// <summary>Form fields with examples.</summary>
    /// <param name="title" example="Report">A renamed field.</param>
    /// <param name="pages" example="12">A number.</param>
    /// <param name="note">No example.</param>
    [HttpPost("form")]
    public IActionResult Form([FromForm(Name = "doc_title")] string title, [FromForm] int pages, [FromForm] string? note) => Ok();

    /// <summary>A single form field with an example.</summary>
    /// <param name="pages" example="3">A number.</param>
    [HttpPost("form-single")]
    public IActionResult FormSingle([FromForm] int pages) => Ok();

    /// <summary>Form fields whose examples do not parse.</summary>
    /// <param name="title" example="Report">A renamed field.</param>
    /// <param name="pages" example="many">Not a number.</param>
    /// <param name="copies" example="x">Not a number.</param>
    [HttpPost("form-broken")]
    public IActionResult FormBroken([FromForm(Name = "doc_title")] string title, [FromForm] int pages, [FromForm] int copies) => Ok();

    /// <summary>A complex form parameter.</summary>
    /// <param name="upload" example='{"label": "from the form"}'>The upload.</param>
    [HttpPost("form-complex")]
    public IActionResult FormComplex([FromForm] ExampleTarget upload) => Ok();

    /// <summary>Renamed parameters keep their XML description.</summary>
    /// <param name="a">The header a.</param>
    /// <param name="b">The query b.</param>
    [HttpGet("renamed")]
    public IActionResult Renamed([FromHeader(Name = "X-A")] string a, [FromQuery(Name = "q")] string b) => Ok();
}
