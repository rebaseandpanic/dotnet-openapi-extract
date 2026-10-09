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

    /// <summary>Extension data shapes.</summary>
    [HttpGet("extension/object")]
    public ActionResult<ExtensionObjectBag> ExtensionObject() => new ExtensionObjectBag { Name = "n" };

    /// <summary>Extension data shapes.</summary>
    [HttpGet("extension/element")]
    public ActionResult<ExtensionElementBag> ExtensionElement() => new ExtensionElementBag { Name = "n" };

    /// <summary>Extension data shapes.</summary>
    [HttpGet("extension/dictionary")]
    public ActionResult<ExtensionDictionaryBag> ExtensionDictionary() => new ExtensionDictionaryBag { Name = "n" };

    /// <summary>Extension data shapes.</summary>
    [HttpGet("extension/json-object")]
    public ActionResult<ExtensionJsonObjectBag> ExtensionJsonObject() => new ExtensionJsonObjectBag { Name = "n" };

    /// <summary>Extension data shapes.</summary>
    [HttpGet("extension/derived")]
    public ActionResult<ExtensionDerivedBag> ExtensionDerived() => new ExtensionDerivedBag { Name = "n" };

    /// <summary>Extension data shapes.</summary>
    [HttpGet("extension/ignored")]
    public ActionResult<ExtensionIgnoredBag> ExtensionIgnored() => new ExtensionIgnoredBag { Name = "n" };

    /// <summary>Extension data shapes.</summary>
    [HttpGet("extension/generic")]
    public ActionResult<ExtensionEnvelope<Base64Payload>> ExtensionGeneric() =>
        new ExtensionEnvelope<Base64Payload> { Payload = new Base64Payload { Data = [1], Token = "AQ==", Plain = "x" } };
}
