using LegacyNoNrtApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace LegacyNoNrtApi.Controllers;

/// <summary>Properties, bodies and responses of collections, arrays and dictionaries without nullable reference types.</summary>
[ApiController]
[Route("legacy")]
public class LegacyCollectionsController : ControllerBase
{
    /// <summary>A DTO with collections, arrays and dictionaries.</summary>
    [HttpGet("model")]
    public LegacyCollectionsModel Model() => new();

    /// <summary>A body of strings.</summary>
    [HttpPost("body-strings")]
    public IActionResult BodyStrings([FromBody] List<string> body) => Ok();

    /// <summary>A body of item values.</summary>
    [HttpPost("body-dictionary")]
    public IActionResult BodyDictionary([FromBody] Dictionary<string, LegacyItem> body) => Ok();

    /// <summary>A body of lists of items.</summary>
    [HttpPost("body-nested")]
    public IActionResult BodyNested([FromBody] List<List<LegacyItem>> body) => Ok();

    /// <summary>A body of an array of items.</summary>
    [HttpPost("body-array")]
    public IActionResult BodyArray([FromBody] LegacyItem[] body) => Ok();

    /// <summary>A response of strings.</summary>
    [HttpGet("response-strings")]
    public List<string> ResponseStrings() => [];

    /// <summary>A response of item values.</summary>
    [HttpGet("response-dictionary")]
    public Dictionary<string, LegacyItem> ResponseDictionary() => [];

    /// <summary>A response of lists of items.</summary>
    [HttpGet("response-nested")]
    public List<List<LegacyItem>> ResponseNested() => [];

    /// <summary>A response of an array of items.</summary>
    [HttpGet("response-array")]
    public LegacyItem[] ResponseArray() => [];
}
