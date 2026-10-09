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

    /// <summary>Numeric ranges.</summary>
    [HttpGet("range")]
    public ActionResult<RangeModel> Range() => new RangeModel();

    /// <summary>Dictionary keys.</summary>
    [HttpGet("dictionaries")]
    public ActionResult<DictionaryModel> Dictionaries() => new DictionaryModel();

    /// <summary>Allowed and denied values.</summary>
    [HttpGet("allowed-values")]
    public ActionResult<AllowedValuesModel> AllowedValues() => new AllowedValuesModel();

    /// <summary>Unconstrained values and 64-bit enums.</summary>
    [HttpGet("loose-values")]
    public ActionResult<LooseValuesModel> LooseValues() => new LooseValuesModel();

    /// <summary>Defaults on DTO properties.</summary>
    [HttpGet("defaults")]
    public ActionResult<DefaultValuesModel> Defaults() => new DefaultValuesModel();

    /// <summary>Defaults on action parameters.</summary>
    [HttpGet("parameter-defaults")]
    public IActionResult ParameterDefaults(
        [FromQuery, System.ComponentModel.DefaultValue(typeof(decimal), "1.5")] decimal rate,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(int), "42")] int count,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(bool), "true")] bool enabled,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(string), "hello")] string greeting,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(Guid), "0f8fad5b-d9cb-469f-a165-70867728950e")] Guid id,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(int), "many")] int broken,
        [FromQuery, System.ComponentModel.DefaultValue(5)] int literal,
        [FromQuery] int page = 1) => Ok();

    /// <summary>Allowed and denied ulong enum members.</summary>
    [HttpGet("huge-code-values")]
    public ActionResult<HugeCodeValuesModel> HugeCodeValues() => new HugeCodeValuesModel();
}
