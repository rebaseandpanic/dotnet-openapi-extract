using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi for every version, with diagnostics.</summary>
public sealed class AllowedValuesFixture
{
    public AllowedValuesFixture()
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
            Builds[version] = (VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult(), diagnostics);
        }
    }

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];
}

/// <summary>
/// <c>[AllowedValues]</c> gives <c>enum</c> / <c>const</c> typed by the schema, <c>[DeniedValues]</c>
/// <c>not: {enum}</c>; both combine with the schema's own constraints by AND, composing where an
/// <c>enum</c> already exists, and values of another JSON type give a warning and no constraint.
/// </summary>
public class AllowedDeniedValuesSchemaTests(AllowedValuesFixture fixture) : IClassFixture<AllowedValuesFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Property(OpenApiSpecVersion version, string name) =>
        fixture.Builds[version].Document["components"]!["schemas"]!["AllowedValuesModel"]!["properties"]![name]!.AsObject();

    private static IReadOnlyList<string> Json(JsonNode? node) => node!.AsArray().Select(v => v!.ToJsonString()).ToList();

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void AllowedValues_AreAnEnumOfTheSchemaType_NextToOtherKeywords(OpenApiSpecVersion version)
    {
        var color = Property(version, "color");
        Json(color["enum"]).Should().Equal("\"red\"", "\"green\"");
        color["maxLength"]!.GetValue<int>().Should().Be(10);
        color["type"]!.GetValue<string>().Should().Be("string");

        Json(Property(version, "level")["enum"]).Should().Equal(["1", "2"], because: "numbers stay numbers");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SingleAllowedValue_IsConstFrom31_AndAOneElementEnumIn30(OpenApiSpecVersion version)
    {
        var single = Property(version, "single");
        if (version == OpenApiSpecVersion.OpenApi3_0)
        {
            Json(single["enum"]).Should().Equal(["\"only\""]);
            single["const"].Should().BeNull();
        }
        else
        {
            single["const"]!.GetValue<string>().Should().Be("only");
            single["enum"].Should().BeNull();
        }

        Json(Property(version, "lucky")["enum"]).Should().Equal(["7"], because: "an integer is always a one-element enum");
        Property(version, "lucky")["const"].Should().BeNull();

        fixture.Builds[version].Diagnostics.Should().NotContain(d => (d.Location ?? "").Contains("AllowedValuesModel/properties/single")
                                                                   || (d.Location ?? "").Contains("AllowedValuesModel/properties/lucky"));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void DeniedValues_AreANotEnum(OpenApiSpecVersion version)
    {
        var userName = Property(version, "userName");
        Json(userName["not"]!["enum"]).Should().Equal("\"admin\"", "\"root\"");
        userName["type"]!.GetValue<string>().Should().Be("string");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void AllowedValuesOnAnEnumType_ComposeWithItsOwnEnum(OpenApiSpecVersion version)
    {
        var allOf = Property(version, "speed")["allOf"]!.AsArray();

        allOf.Should().HaveCount(2);
        Json(allOf[0]!["enum"]).Should().Equal(["0", "1", "2"], because: "the type's own enum is kept whole");
        Json(allOf[1]!["enum"]).Should().Equal(["0", "1"], because: "the allowed values are a second schema, not an intersection");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void AllowedValuesOnAReference_AreAnElementOfItsAllOfWrapper(OpenApiSpecVersion version)
    {
        var target = Property(version, "target");
        var allOf = target["allOf"]!.AsArray();

        allOf[0]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/AllowedTarget");
        target["$ref"].Should().BeNull();
        target["enum"].Should().BeNull(because: "the values are not a sibling of the reference");
        target["const"].Should().BeNull();
        allOf.Should().HaveCount(2);
        var constraint = allOf[1]!;
        (constraint["const"]?.GetValue<string>() ?? constraint["enum"]![0]!.GetValue<string>()).Should().Be("x");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ValuesOfAnotherJsonType_GiveNoConstraintAndOneWarning(OpenApiSpecVersion version)
    {
        var mismatched = Property(version, "mismatched");
        mismatched["enum"].Should().BeNull();
        mismatched["allOf"].Should().BeNull();

        fixture.Builds[version].Diagnostics
            .Where(d => d.Location == "#/components/schemas/AllowedValuesModel/properties/mismatched")
            .Should().ContainSingle().Which.Code.Should().Be(ExtractionDiagnosticCodes.SchemaValueNotConvertible);
    }
}
