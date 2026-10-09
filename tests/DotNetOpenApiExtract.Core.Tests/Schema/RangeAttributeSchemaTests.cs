using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi for every version: raw JSON text, parsed document and diagnostics.</summary>
public sealed class RangeSchemaFixture
{
    public RangeSchemaFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Text[version] = document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult();
            Documents[version] = JsonNode.Parse(Text[version])!;
            Diagnostics[version] = diagnostics;
        }
    }

    public Dictionary<OpenApiSpecVersion, string> Text { get; } = [];

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];
}

/// <summary>
/// <c>[Range]</c> is translated exactly: every overload, exclusive sides in the version's form, string
/// bounds in the invariant culture without binary floating point; declarations RangeAttribute rejects
/// are extraction errors, bounds JSON cannot hold are dropped with a warning.
/// </summary>
public class RangeAttributeSchemaTests(RangeSchemaFixture fixture) : IClassFixture<RangeSchemaFixture>
{
    private const string Pointer = "#/components/schemas/RangeModel/properties/";
    private const string RangeCode = ExtractionDiagnosticCodes.SchemaRangeNotExpressible;

    private JsonObject Property(OpenApiSpecVersion version, string name) =>
        fixture.Documents[version]["components"]!["schemas"]!["RangeModel"]!["properties"]![name]!.AsObject();

    private static IReadOnlyDictionary<string, string> Bounds(JsonObject schema) =>
        schema.Where(p => p.Key is "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum")
            .ToDictionary(p => p.Key, p => p.Value!.ToJsonString());

    public static TheoryData<OpenApiSpecVersion, string, string[], string[]> BoundCases
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, string[], string[]>();
            data.Add(OpenApiSpecVersion.OpenApi3_0, "inclusive", ["minimum=1", "maximum=10"], []);
            data.Add(OpenApiSpecVersion.OpenApi3_0, "exclusiveMin", ["minimum=1", "exclusiveMinimum=true", "maximum=10"], []);
            data.Add(OpenApiSpecVersion.OpenApi3_0, "exclusiveMax", ["minimum=0.5", "maximum=9.5", "exclusiveMaximum=true"], []);
            data.Add(OpenApiSpecVersion.OpenApi3_0, "exclusiveBoth",
                ["minimum=0.01", "exclusiveMinimum=true", "maximum=999.99", "exclusiveMaximum=true"], []);
            foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
            {
                data.Add(version, "inclusive", ["minimum=1", "maximum=10"], []);
                data.Add(version, "exclusiveMin", ["exclusiveMinimum=1", "maximum=10"], ["minimum"]);
                data.Add(version, "exclusiveMax", ["minimum=0.5", "exclusiveMaximum=9.5"], ["maximum"]);
                data.Add(version, "exclusiveBoth", ["exclusiveMinimum=0.01", "exclusiveMaximum=999.99"], ["minimum", "maximum"]);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(BoundCases))]
    public void Bounds_HaveTheFormOfTheVersion_WithoutWarnings(OpenApiSpecVersion version, string property, string[] expected, string[] absent)
    {
        var bounds = Bounds(Property(version, property));

        bounds.Select(b => $"{b.Key}={b.Value}").Should().BeEquivalentTo(expected);
        foreach (var key in absent)
            bounds.Keys.Should().NotContain(key);
        fixture.Diagnostics[version].Should().NotContain(d => d.Location == Pointer + property);
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void StringBounds_AreExactLexemes(OpenApiSpecVersion version)
    {
        Bounds(Property(version, "price")).Should().Equal(new Dictionary<string, string> { ["minimum"] = "0.01", ["maximum"] = "999.99" });
        fixture.Text[version].Should().Contain("\"minimum\": 0.1234567890123456789012345678",
            because: "a decimal bound keeps its 28 significant digits");
        Bounds(Property(version, "optional")).Should().Equal(new Dictionary<string, string> { ["minimum"] = "-5", ["maximum"] = "5" });
    }

    [Fact]
    public void StringBounds_DoNotDependOnTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var text = OpenApiDocumentBuilder.Build(VersionedDocumentHarness.ModernApiOptions(OpenApiSpecVersion.OpenApi3_1))
                .SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult();
            var price = JsonNode.Parse(text)!["components"]!["schemas"]!["RangeModel"]!["properties"]!["price"]!.AsObject();

            Bounds(price).Should().Equal(new Dictionary<string, string> { ["minimum"] = "0.01", ["maximum"] = "999.99" });
            text.Should().Contain("0.1234567890123456789012345678");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NonNumericOperand_GivesNoBoundsAndOneWarning(OpenApiSpecVersion version)
    {
        Bounds(Property(version, "when")).Should().BeEmpty();
        fixture.Diagnostics[version].Where(d => d.Location == Pointer + "when").Should().ContainSingle()
            .Which.Code.Should().Be(RangeCode);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NonFiniteBound_IsDropped_TheOtherKept_OneWarning(OpenApiSpecVersion version)
    {
        foreach (var property in new[] { "unbounded", "notANumberMin" })
        {
            Bounds(Property(version, property)).Should().Equal(new Dictionary<string, string> { ["maximum"] = "5" }, because: property);
            fixture.Diagnostics[version].Where(d => d.Location == Pointer + property).Should().ContainSingle(because: property)
                .Which.Code.Should().Be(RangeCode);
        }

        // RangeAttribute accepts both declarations.
        ValidateWithAttribute(typeof(RangeModel), nameof(RangeModel.Unbounded), 1.0).Should().NotThrow();
        ValidateWithAttribute(typeof(RangeModel), nameof(RangeModel.NotANumberMin), 1.0).Should().NotThrow();
        fixture.Text[version].Should().NotMatchRegex(@":\s*-?(Infinity|NaN)\b", because: "JSON has no such numbers");
    }

    // ── Declarations RangeAttribute itself rejects ──────────────────────────

    public sealed class MinimumAboveMaximum { [Range(5, 1)] public int Value { get; set; } }

    public sealed class EqualWithExclusive { [Range(5, 5, MinimumIsExclusive = true)] public int Value { get; set; } }

    public sealed class Unparseable { [Range(typeof(decimal), "abc", "5")] public decimal Value { get; set; } }

    public sealed class NotANumberMaximum { [Range(0.0, double.NaN)] public double Value { get; set; } }

    public sealed class EqualInclusive { [Range(5, 5)] public int Value { get; set; } }

    private static Action ValidateWithAttribute(Type type, string property, object value) =>
        () => type.GetProperty(property)!.GetCustomAttribute<RangeAttribute>()!.IsValid(value);

    public static TheoryData<Type, object, bool> Declarations => new()
    {
        { typeof(MinimumAboveMaximum), 3, true },
        { typeof(EqualWithExclusive), 5, true },
        { typeof(Unparseable), 1m, true },
        { typeof(NotANumberMaximum), 0.5, true },
        { typeof(EqualInclusive), 5, false },
    };

    [Theory]
    [MemberData(nameof(Declarations))]
    public void ExtractionError_ExactlyWhenRangeAttributeRejectsTheDeclaration(Type type, object value, bool rejected)
    {
        var attribute = ValidateWithAttribute(type, "Value", value);
        var generate = () => new SchemaGenerator().GenerateSchema(type);

        if (rejected)
        {
            attribute.Should().Throw<Exception>();
            var error = generate.Should().Throw<OpenApiExtractionException>().Which;
            error.TypeName.Should().Be(type.FullName);
            error.MemberName.Should().Be("Value");
        }
        else
        {
            attribute.Should().NotThrow();
            generate.Should().NotThrow();
        }
    }
}
