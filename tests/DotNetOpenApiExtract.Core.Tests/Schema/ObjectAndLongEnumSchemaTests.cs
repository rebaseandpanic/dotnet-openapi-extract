using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi for every version: raw JSON text and parsed document.</summary>
public sealed class LooseValuesFixture
{
    public LooseValuesFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            Text[version] = OpenApiDocumentBuilder.Build(VersionedDocumentHarness.ModernApiOptions(version))
                .SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult();
            Documents[version] = JsonNode.Parse(Text[version])!;
        }
    }

    public Dictionary<OpenApiSpecVersion, string> Text { get; } = [];

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];
}

/// <summary>
/// <c>object</c> / <c>dynamic</c> properties are unconstrained (<c>{}</c>), and enums over
/// <c>long</c> / <c>ulong</c> keep their values with the type and format of their underlying type.
/// </summary>
public class ObjectAndLongEnumSchemaTests(LooseValuesFixture fixture) : IClassFixture<LooseValuesFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Property(OpenApiSpecVersion version, string name) =>
        fixture.Documents[version]["components"]!["schemas"]!["LooseValuesModel"]!["properties"]![name]!.AsObject();

    private JsonObject Component(OpenApiSpecVersion version, string name)
    {
        var property = Property(version, name);
        var reference = (property["$ref"] ?? property["allOf"]?[0]?["$ref"])?.GetValue<string>();
        return reference == null
            ? property
            : fixture.Documents[version]["components"]!["schemas"]![reference.Split('/')[^1]]!.AsObject();
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ObjectAndDynamic_AreUnconstrained(OpenApiSpecVersion version)
    {
        foreach (var name in new[] { "anything", "dynamic" })
        {
            var schema = Property(version, name);
            schema["type"].Should().BeNull(because: name);
            schema.Select(p => p.Key).Should().BeSubsetOf(["description"], because: $"{name} holds any JSON value");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void LongEnum_KeepsItsValues_WithTheFormOfLong(OpenApiSpecVersion version)
    {
        var wide = Component(version, "wide");
        var plain = Property(version, "plainLong");

        wide["type"]!.GetValue<string>().Should().Be(plain["type"]!.GetValue<string>());
        wide["format"]!.GetValue<string>().Should().Be(plain["format"]!.GetValue<string>()).And.Be("int64");
        wide["enum"]!.AsArray().Select(v => v!.ToJsonString()).Should().Equal("-9223372036854775808", "9223372036854775807");
        fixture.Text[version].Should().Contain("9223372036854775807");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void UlongEnum_KeepsItsValues_WithTheFormOfUlong(OpenApiSpecVersion version)
    {
        var huge = Component(version, "huge");
        var plain = Property(version, "plainUlong");

        huge["type"]!.GetValue<string>().Should().Be(plain["type"]!.GetValue<string>());
        huge["format"].Should().BeNull(because: "no OpenAPI format holds ulong.MaxValue (int64 does not)");
        plain["format"].Should().BeNull(because: "the same form as a ulong property");
        huge["enum"]!.AsArray().Select(v => v!.ToJsonString()).Should().Equal("0", "18446744073709551615");
        fixture.Text[version].Should().Contain("18446744073709551615", because: "the value is written as a number lexeme without loss");
    }
}
