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
            Builds[version] = Build(version);

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

    public (string Text, JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) German { get; }

    private static (string, JsonNode, IReadOnlyList<ExtractionDiagnostic>) Build(OpenApiSpecVersion version)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            XmlPath        = TestPaths.ModernApiXml,
            OpenApiVersion = version,
            OnDiagnostic   = onDiagnostic,
        });
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
        warnings.Where(d => d.Location == "GET /keywords/parameter-defaults" && d.Subjects.SequenceEqual(new[] { "broken" }))
            .Should().ContainSingle();
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
                               && d.Location == "GET /keywords/parameter-defaults" && d.Subjects.SequenceEqual(new[] { "home" }))
            .Should().ContainSingle();
    }
}
