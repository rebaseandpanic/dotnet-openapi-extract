using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi for every version (with diagnostics), and 3.1 with an unknown global converter.</summary>
public sealed class DictionaryKeyFixture
{
    public DictionaryKeyFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
            Builds[version] = Build(version, null);

        WithUnknownGlobalConverter = Build(OpenApiSpecVersion.OpenApi3_1, """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new TenantIdConverter());
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);
    }

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];

    public (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) WithUnknownGlobalConverter { get; }

    private static (JsonNode, IReadOnlyList<ExtractionDiagnostic>) Build(OpenApiSpecVersion version, string? programCs)
    {
        using var source = new TempDirectory();
        File.WriteAllText(Path.Combine(source.Path, "Program.cs"), programCs ?? """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            XmlPath        = TestPaths.ModernApiXml,
            SourceRoot     = source.Path,
            OpenApiVersion = version,
            OnDiagnostic   = onDiagnostic,
        });
        var json = VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
        return (json, diagnostics);
    }
}

/// <summary>
/// Dictionary keys get a <c>propertyNames</c> constraint in 3.1+ that is never narrower than what
/// System.Text.Json writes and accepts; 3.0 has none, without a warning. <c>[MinLength]</c> /
/// <c>[MaxLength]</c> on a dictionary bound its number of properties.
/// </summary>
public class DictionaryKeySchemaTests(DictionaryKeyFixture fixture) : IClassFixture<DictionaryKeyFixture>
{
    private const string KeyConverterCode = ExtractionDiagnosticCodes.SchemaUnknownKeyConverter;

    private static JsonObject Property(JsonNode document, string name) =>
        document["components"]!["schemas"]!["DictionaryModel"]!["properties"]![name]!.AsObject();

    private const string Signed = "^[+-]?[0-9]+$";
    private const string Unsigned = "^\\+?[0-9]+$";

    public static TheoryData<OpenApiSpecVersion, string, string?, string?> KeyCases
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, string?, string?>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                var v30 = version == OpenApiSpecVersion.OpenApi3_0;
                data.Add(version, "byGuid", v30 ? null : "uuid", null);
                data.Add(version, "byInt", null, v30 ? null : Signed);
                data.Add(version, "byLong", null, v30 ? null : Signed);
                data.Add(version, "byUint", null, v30 ? null : Unsigned);
                data.Add(version, "swapped", null, v30 ? null : Signed);
                data.Add(version, "byName", null, null);
                data.Add(version, "byDay", null, null);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KeyCases))]
    public void Keys_GetTheConstraintOfTheirType(OpenApiSpecVersion version, string property, string? format, string? pattern)
    {
        var schema = Property(fixture.Builds[version].Document, property);
        var names = schema["propertyNames"];

        if (format == null && pattern == null)
        {
            names.Should().BeNull(because: property);
            schema.Select(p => p.Key).Should().NotContain(k => k.Contains("propertyNames", StringComparison.Ordinal));
            return;
        }

        names!["type"]!.GetValue<string>().Should().Be("string");
        names["format"]?.GetValue<string>().Should().Be(format);
        names["pattern"]?.GetValue<string>().Should().Be(pattern);
        (names["format"] ?? names["pattern"]).Should().NotBeNull();
    }

    [Fact]
    public void Version30_HasNoKeyConstraintAndNoWarning()
    {
        fixture.Builds[OpenApiSpecVersion.OpenApi3_0].Diagnostics
            .Should().NotContain(d => d.Feature == "schema.propertyNames" || (d.Location ?? "").Contains("DictionaryModel"));
    }

    [Fact]
    public void KeyTypeWithAnUnknownConverter_HasNoConstraintAndOneWarning()
    {
        var (document, diagnostics) = fixture.Builds[OpenApiSpecVersion.OpenApi3_1];

        Property(document, "byRegion")["propertyNames"].Should().BeNull();
        var warning = diagnostics.Where(d => d.Code == KeyConverterCode).Should().ContainSingle().Subject;
        warning.Subjects.Should().Equal("ModernApi.Models.Keywords.RegionCode", "ModernApi.Models.Keywords.RegionCodeConverter");
    }

    [Fact]
    public void UnknownGlobalConverter_LeavesIntegerAndGuidKeysUnconstrained_WithAWarning()
    {
        var (document, diagnostics) = fixture.WithUnknownGlobalConverter;

        foreach (var property in new[] { "byGuid", "byInt", "byLong", "byUint", "swapped" })
            Property(document, property)["propertyNames"].Should().BeNull(because: property);
        diagnostics.Where(d => d.Code == KeyConverterCode).Select(d => d.Subjects[0]).Should().Contain("System.Int32");
    }

    // ── D14 ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void LengthAttributesOnADictionary_BoundItsProperties(OpenApiSpecVersion version)
    {
        var schema = Property(fixture.Builds[version].Document, "limited");

        schema["minProperties"]!.GetValue<int>().Should().Be(1);
        schema["maxProperties"]!.GetValue<int>().Should().Be(5);
        schema.Select(p => p.Key).Should().NotContain(["minLength", "maxLength"]);
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    // ── SC-003: keys STJ writes and accepts pass, keys it rejects fail ───────

    /// <summary>
    /// Key inputs: whether STJ 10 accepts them (measured in the test) and whether the schema does. The
    /// schema is never narrower than STJ; it may be wider (the unsigned pattern admits <c>+7</c>, which
    /// STJ rejects; ranges are not clamped).
    /// </summary>
    public static TheoryData<OpenApiSpecVersion, string, Type, string, bool, bool> KeyInputs
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, Type, string, bool, bool>();
            foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
            {
                data.Add(version, "byInt", typeof(Dictionary<int, string>), "+1", true, true);
                data.Add(version, "byInt", typeof(Dictionary<int, string>), "01", true, true);
                data.Add(version, "byInt", typeof(Dictionary<int, string>), "-5", true, true);
                data.Add(version, "byInt", typeof(Dictionary<int, string>), "1.5", false, false);
                data.Add(version, "byUint", typeof(Dictionary<uint, int>), "07", true, true);
                data.Add(version, "byUint", typeof(Dictionary<uint, int>), "+7", false, true);
                data.Add(version, "byUint", typeof(Dictionary<uint, int>), "-1", false, false);
                data.Add(version, "byGuid", typeof(Dictionary<Guid, int>), "0f8fad5b-d9cb-469f-a165-70867728950e", true, true);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KeyInputs))]
    public void KeySchema_IsNeverNarrowerThanStj(
        OpenApiSpecVersion version, string property, Type dictionary, string key, bool stjAccepts, bool schemaAccepts)
    {
        var valueJson = dictionary.GetGenericArguments()[1] == typeof(string) ? "\"v\"" : "1";
        var json = $"{{\"{key}\":{valueJson}}}";
        StjWire.Accepts(json, dictionary, StjWire.Mvc()).Should().Be(stjAccepts, because: "the reference is the real serializer");

        var document = fixture.Builds[version].Document;
        var result = SchemaConformance.For(document).ValidateSchema(Property(document, property), JsonNode.Parse(json));

        result.IsValid.Should().Be(schemaAccepts, because: $"{key}: {string.Join("; ", result.Errors)}");
        if (stjAccepts)
            result.IsValid.Should().BeTrue(because: "a key STJ accepts must pass the schema");
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void KeysStjWrites_PassTheSchema(OpenApiSpecVersion version)
    {
        var document = fixture.Builds[version].Document;
        var conformance = SchemaConformance.For(document);

        var written = new Dictionary<string, JsonNode?>
        {
            ["byGuid"] = StjWire.Serialize(new Dictionary<Guid, int> { [Guid.NewGuid()] = 1 }, StjWire.Mvc()),
            ["byInt"] = StjWire.Serialize(new Dictionary<int, string> { [-42] = "a", [7] = "b" }, StjWire.Mvc()),
            ["byUint"] = StjWire.Serialize(new Dictionary<uint, int> { [uint.MaxValue] = 1 }, StjWire.Mvc()),
        };
        foreach (var (property, value) in written)
        {
            var result = conformance.ValidateSchema(Property(document, property), value);
            result.IsValid.Should().BeTrue(because: $"{property}: {string.Join("; ", result.Errors)}");
        }
    }

    public static TheoryData<OpenApiSpecVersion> SchemaVersions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];
}
