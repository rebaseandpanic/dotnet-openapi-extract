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
        [FromQuery, System.ComponentModel.DefaultValue(typeof(ShippingSpeed), "Express")] ShippingSpeed speed,
        [FromQuery, System.ComponentModel.DefaultValue(ShippingSpeed.Overnight)] ShippingSpeed literalSpeed,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(DateTime), "01/02/2024")] DateTime since,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(TimeSpan), "1:02:03")] TimeSpan timeout,
        [FromQuery, System.ComponentModel.DefaultValue(typeof(Uri), "https://example.com")] Uri? home,
        [FromQuery] int page = 1) => Ok();

    /// <summary>Allowed and denied ulong enum members.</summary>
    [HttpGet("huge-code-values")]
    public ActionResult<HugeCodeValuesModel> HugeCodeValues() => new HugeCodeValuesModel();

    /// <summary>Enum defaults on a property and on parameters.</summary>
    [HttpGet("enum-defaults")]
    public ActionResult<EnumDefaultsModel> EnumDefaults(
        [FromQuery] DeliveryMode mode = DeliveryMode.Courier,
        [FromQuery] ShippingSpeed speed = ShippingSpeed.Overnight) => new EnumDefaultsModel();

    /// <summary>[Length] on each schema shape.</summary>
    [HttpGet("lengths")]
    public ActionResult<LengthModel> Lengths() => new LengthModel();

    /// <summary>[DataType] with every member.</summary>
    [HttpGet("data-types")]
    public ActionResult<DataTypeModel> DataTypes() => new DataTypeModel();

    /// <summary>Competing format sources.</summary>
    [HttpGet("format-priority")]
    public ActionResult<FormatPriorityModel> FormatPriority() => new FormatPriorityModel();

    /// <summary>Competing description sources.</summary>
    [HttpGet("description-priority")]
    public ActionResult<DescriptionPriorityModel> DescriptionPriority() => new DescriptionPriorityModel();

    /// <summary>readOnly, writeOnly and title sources.</summary>
    [HttpGet("access")]
    public ActionResult<AccessModel> Access() => new AccessModel();

    /// <summary>Enum members under every known converter.</summary>
    [HttpGet("enum-wire-names")]
    public ActionResult<EnumWireNamesModel> EnumWireNames(
        [FromQuery] StjTint tint = StjTint.Red,
        [FromQuery] StjTint[]? tints = null) => new EnumWireNamesModel();

    /// <summary>XML examples of every JSON type.</summary>
    [HttpGet("examples")]
    public ActionResult<ExampleModel> Examples() => new ExampleModel();

    /// <summary>XML examples on a positional record.</summary>
    [HttpGet("record-examples")]
    public ActionResult<ExampleRecord> RecordExamples() => new ExampleRecord(1, "EUR");

    /// <summary>XML examples that cannot be written.</summary>
    [HttpGet("example-failures")]
    public ActionResult<ExampleFailuresModel> ExampleFailures() => new ExampleFailuresModel();

    /// <summary>Constraints and examples on numbers read from strings.</summary>
    [HttpGet("number-placement")]
    public ActionResult<NumberPlacementModel> NumberPlacement() => new NumberPlacementModel();

    /// <summary>[Length] next to the other length attributes.</summary>
    [HttpGet("length-intersection")]
    public ActionResult<LengthIntersectionModel> LengthIntersection() => new LengthIntersectionModel();

    /// <summary>A polymorphic base with an example.</summary>
    [HttpGet("example-shape")]
    public ActionResult<ExampleShape> ExampleShape() => new ExampleCircle();

    /// <summary>A polymorphic base whose examples cannot be written.</summary>
    [HttpGet("example-broken-shape")]
    public ActionResult<ExampleBrokenShape> ExampleBrokenShape() => new ExampleBrokenSquare();

    /// <summary>A polymorphic base whose example is not an object.</summary>
    [HttpGet("numberexampleshape")]
    public ActionResult<NumberExampleShape> NumberExampleShape() => new NumberExampleShapeItem();

    /// <summary>A polymorphic base whose example is not an object.</summary>
    [HttpGet("booleanexampleshape")]
    public ActionResult<BooleanExampleShape> BooleanExampleShape() => new BooleanExampleShapeItem();

    /// <summary>A polymorphic base whose example is not an object.</summary>
    [HttpGet("stringexampleshape")]
    public ActionResult<StringExampleShape> StringExampleShape() => new StringExampleShapeItem();

    /// <summary>A polymorphic base whose example is not an object.</summary>
    [HttpGet("arrayexampleshape")]
    public ActionResult<ArrayExampleShape> ArrayExampleShape() => new ArrayExampleShapeItem();
}
