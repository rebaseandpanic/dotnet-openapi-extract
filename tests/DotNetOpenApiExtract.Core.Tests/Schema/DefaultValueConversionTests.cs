using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi for every version (raw text, document, diagnostics), and 3.1 built under de-DE.</summary>
public sealed class DefaultValuesFixture
{
    public DefaultValuesFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => Options(version, onDiagnostic));
            var text = document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult();
            Builds[version] = (text, JsonNode.Parse(text)!, diagnostics);
            Yaml[version] = VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Yaml, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            German = Build(OpenApiSpecVersion.OpenApi3_1);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    public Dictionary<OpenApiSpecVersion, (string Text, JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];

    /// <summary>The same documents written as YAML and read back with the YAML reader stack of <c>validate</c>.</summary>
    public Dictionary<OpenApiSpecVersion, JsonNode> Yaml { get; } = [];

    public (string Text, JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) German { get; }

    private static OpenApiDocumentOptions Options(OpenApiSpecVersion version, Action<ExtractionDiagnostic> onDiagnostic) => new()
    {
        AssemblyPath   = TestPaths.ModernApiDll,
        XmlPath        = TestPaths.ModernApiXml,
        OpenApiVersion = version,
        OnDiagnostic   = onDiagnostic,
    };

    private static (string, JsonNode, IReadOnlyList<ExtractionDiagnostic>) Build(OpenApiSpecVersion version)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => Options(version, onDiagnostic));
        var text = document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult();
        return (text, JsonNode.Parse(text)!, diagnostics);
    }
}

/// <summary>
/// <c>[DefaultValue]</c> gives the same <c>default</c> on a DTO property and on an action parameter:
/// <c>(Type, string)</c> converted to the type in the invariant culture with the schema's JSON type; a
/// value that does not convert gives one warning and no <c>default</c>.
/// </summary>
public class DefaultValueConversionTests(DefaultValuesFixture fixture) : IClassFixture<DefaultValuesFixture>
{
    /// <summary>The one table of expected defaults, as JSON text.</summary>
    public static TheoryData<OpenApiSpecVersion, string, string> Expected
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, string>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                data.Add(version, "rate", "1.5");
                data.Add(version, "count", "42");
                data.Add(version, "enabled", "true");
                data.Add(version, "greeting", "\"hello\"");
                data.Add(version, "id", "\"0f8fad5b-d9cb-469f-a165-70867728950e\"");
                data.Add(version, "literal", "5");
                data.Add(version, "speed", "1");
                data.Add(version, "literalSpeed", "2");
                data.Add(version, "since", "\"2024-01-02T00:00:00\"");
                data.Add(version, "timeout", "\"01:02:03\"");
            }
            return data;
        }
    }

    private static JsonNode? PropertyDefault(JsonNode document, string name) =>
        document["components"]!["schemas"]!["DefaultValuesModel"]!["properties"]![name]!["default"];

    private static JsonNode? ParameterDefault(JsonNode document, string name) =>
        document["paths"]!["/keywords/parameter-defaults"]!["get"]!["parameters"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == name)!["schema"]!["default"];

    [Theory]
    [MemberData(nameof(Expected))]
    public void PropertyAndParameter_GetTheSameDefault(OpenApiSpecVersion version, string name, string expectedJson)
    {
        var document = fixture.Builds[version].Document;

        PropertyDefault(document, name)!.ToJsonString().Should().Be(expectedJson, because: $"property {name}");
        ParameterDefault(document, name)!.ToJsonString().Should().Be(expectedJson, because: $"parameter {name}");
    }

    [Fact]
    public void Defaults_DoNotDependOnTheCurrentCulture()
    {
        var document = fixture.German.Document;

        PropertyDefault(document, "rate")!.ToJsonString().Should().Be("1.5");
        ParameterDefault(document, "rate")!.ToJsonString().Should().Be("1.5");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void UnconvertibleDefault_IsLeftOut_WithOneWarningPerPlace(OpenApiSpecVersion version)
    {
        var (_, document, diagnostics) = fixture.Builds[version];

        PropertyDefault(document, "broken").Should().BeNull();
        ParameterDefault(document, "broken").Should().BeNull();

        var warnings = diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.SchemaDefaultNotConvertible).ToList();
        warnings.Where(d => d.Location == "#/components/schemas/DefaultValuesModel/properties/broken").Should().ContainSingle();
        warnings.Where(d => d.Location == "GET /keywords/parameter-defaults").Should().ContainSingle()
            .Which.Subjects.Should().Contain("broken");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void CSharpParameterDefault_IsUnchanged(OpenApiSpecVersion version)
    {
        ParameterDefault(fixture.Builds[version].Document, "page")!.ToJsonString().Should().Be("1");
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void EnumDefault_OnAStringEnum_IsTheName(OpenApiSpecVersion version)
    {
        PropertyDefault(fixture.Builds[version].Document, "namedSpeed")!.ToJsonString().Should().Be("\"Express\"");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void DefaultOfATypeTheExtractorDoesNotConvert_IsLeftOut_WithOneWarningPerPlace(OpenApiSpecVersion version)
    {
        var (_, document, diagnostics) = fixture.Builds[version];

        PropertyDefault(document, "home").Should().BeNull();
        ParameterDefault(document, "home").Should().BeNull();
        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.SchemaDefaultNotConvertible
                               && d.Location == "#/components/schemas/DefaultValuesModel/properties/home").Should().ContainSingle();
        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.SchemaDefaultNotConvertible
                               && d.Location == "GET /keywords/parameter-defaults")
            .Should().ContainSingle(because: "one warning per place: the operation names every parameter")
            .Which.Subjects.Should().BeEquivalentTo(["broken", "home"]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void EnumDefaults_FollowTheEnumForm_OnPropertyAndOnCSharpParameterDefault(OpenApiSpecVersion version)
    {
        var document = fixture.Builds[version].Document;

        document["components"]!["schemas"]!["EnumDefaultsModel"]!["properties"]!["mode"]!["default"]!.ToJsonString()
            .Should().Be("\"Courier\"", because: "the enum is written as strings");

        var parameters = document["paths"]!["/keywords/enum-defaults"]!["get"]!["parameters"]!.AsArray();
        parameters.Single(p => p!["name"]!.GetValue<string>() == "mode")!["schema"]!["default"]!.ToJsonString()
            .Should().Be("\"Courier\"", because: "a C# default of a string enum is its name");
        parameters.Single(p => p!["name"]!.GetValue<string>() == "speed")!["schema"]!["default"]!.ToJsonString()
            .Should().Be("2", because: "a C# default of a numeric enum stays its number");
    }

    /// <summary>
    /// Every integer width other than <c>int</c> / <c>long</c>, in each form a default is declared:
    /// the literal attribute and the <c>(Type, string)</c> attribute on a property, the C# default and
    /// the <c>(Type, string)</c> attribute on a parameter. The values are the C# values themselves.
    /// </summary>
    public static TheoryData<OpenApiSpecVersion, DocumentFormat, string, string> NarrowIntegers
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, DocumentFormat, string, string>();
            foreach (var version in VersionedDocumentHarness.Versions)
            foreach (var format in new[] { DocumentFormat.Json, DocumentFormat.Yaml })
            foreach (var form in new[] { "Literal", "Text" })
            {
                data.Add(version, format, "byte" + form, "200");
                data.Add(version, format, "sByte" + form, "-5");
                data.Add(version, format, "short" + form, "-300");
                data.Add(version, format, "uShort" + form, "60000");
                data.Add(version, format, "uInt" + form, "4000000000");
                data.Add(version, format, "uLong" + form, "18446744073709551615");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NarrowIntegers))]
    public void NarrowIntegerDefault_IsWrittenAsANumber(OpenApiSpecVersion version, DocumentFormat format, string name, string expected)
    {
        var document = format == DocumentFormat.Json
            ? JsonNode.Parse(fixture.Builds[version].Text)!
            : fixture.Yaml[version];

        var property = document["components"]!["schemas"]!["NarrowIntegerDefaultsModel"]!["properties"]![name]!["default"];
        var parameter = document["paths"]!["/keywords/narrow-integer-parameter-defaults"]!["get"]!["parameters"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == name)!["schema"]!["default"];

        foreach (var (place, value) in new[] { ("property", property), ("parameter", parameter) })
        {
            value.Should().NotBeNull(because: $"{place} {name}");
            value!.GetValueKind().Should().Be(System.Text.Json.JsonValueKind.Number, because: $"{place} {name}");
            value.ToJsonString().Should().Be(expected, because: $"{place} {name}");
        }
    }
}
