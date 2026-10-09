using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Keywords;

namespace ModernApi.Controllers;

/// <summary>Bodies exercising version-dependent schema keywords.</summary>
[ApiController]
[Route("keywords")]
public class SchemaKeywordsController : ControllerBase
{
    /// <summary>Base64 data.</summary>
    [HttpGet("base64")]
    public ActionResult<Base64Payload> Base64() => new Base64Payload { Data = [1], Token = "AQ==", Plain = "x" };
}
