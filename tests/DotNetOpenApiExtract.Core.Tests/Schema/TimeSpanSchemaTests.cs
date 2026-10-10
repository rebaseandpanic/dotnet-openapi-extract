using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi built and serialized for every version.</summary>
public sealed class TimeSpanSchemaFixture
{
    public TimeSpanSchemaFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = VersionedDocumentHarness.Build(VersionedDocumentHarness.ModernApiOptions(version));
            Documents[version] = JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];
}

/// <summary>
/// A <c>TimeSpan</c> is described as what System.Text.Json writes for it — <c>"00:00:05"</c>,
/// <c>"-3.04:05:06"</c>, <c>"00:00:01.5000000"</c> — and not as <c>format: duration</c> (ISO 8601,
/// <c>"PT5S"</c>), which a client that checks formats rejects. Expected values are the real output
/// of <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
public class TimeSpanSchemaTests(TimeSpanSchemaFixture fixture) : IClassFixture<TimeSpanSchemaFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private static readonly TimeSpan[] Values =
    [
        TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(-5), new(3, 4, 5, 6), new(-3, -4, -5, -6),
        TimeSpan.FromTicks(1), TimeSpan.FromTicks(-1), TimeSpan.FromMilliseconds(1500), TimeSpan.MinValue, TimeSpan.MaxValue,
    ];

    /// <summary>What System.Text.Json writes for each of <see cref="Values"/>.</summary>
    private static IEnumerable<string> Written() =>
        Values.Select(v => StjWire.Serialize(v, StjWire.Mvc())!.GetValue<string>());

    private JsonObject Property(OpenApiSpecVersion version, string name) =>
        fixture.Documents[version]["components"]!["schemas"]!["TimeSpanModel"]!["properties"]![name]!.AsObject();

    private JsonObject Parameter(OpenApiSpecVersion version, string name) =>
        fixture.Documents[version]["paths"]!["/keywords/time-spans"]!["post"]!["parameters"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == name)!["schema"]!.AsObject();

    private static void ShouldDescribeWrittenTimeSpans(JsonObject schema, string because)
    {
        schema.ContainsKey("format").Should().BeFalse(because);
        var type = schema["type"]!;
        (type is JsonArray types ? types.Select(t => t!.GetValue<string>()) : [type.GetValue<string>()])
            .Should().Contain("string", because);

        var pattern = new Regex(schema["pattern"]!.GetValue<string>());
        foreach (var written in Written())
            pattern.IsMatch(written).Should().BeTrue($"{because}: System.Text.Json writes \"{written}\"");
        pattern.IsMatch("PT5S").Should().BeFalse(because: "an ISO 8601 duration is not what the server writes");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Properties_AreTheWrittenText_WithoutDurationFormat(OpenApiSpecVersion version)
    {
        ShouldDescribeWrittenTimeSpans(Property(version, "plain"), "TimeSpan");
        ShouldDescribeWrittenTimeSpans(Property(version, "optional"), "TimeSpan?");
        ShouldDescribeWrittenTimeSpans(Property(version, "annotated"), "[DataType(Duration)] TimeSpan");
        Property(version, "isoText")["format"]!.GetValue<string>().Should().Be("duration", because: "on a string the annotation keeps its format");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void QueryParameters_AreTheWrittenText_WithoutDurationFormat(OpenApiSpecVersion version)
    {
        ShouldDescribeWrittenTimeSpans(Parameter(version, "wait"), "TimeSpan parameter");
        ShouldDescribeWrittenTimeSpans(Parameter(version, "timeout"), "[DataType(Duration)] TimeSpan? parameter");
    }

    public static TheoryData<OpenApiSpecVersion> JsonSchemaVersions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    /// <summary>The real JSON of the model, for every value, passes the component (JSON Schema 2020-12, formats asserted).</summary>
    [Theory]
    [MemberData(nameof(JsonSchemaVersions))]
    public void WrittenModel_PassesTheComponent(OpenApiSpecVersion version)
    {
        var conformance = SchemaConformance.For(fixture.Documents[version]);

        foreach (var value in Values)
        {
            var wire = StjWire.Serialize(new TimeSpanModel { Plain = value, Optional = value, Annotated = value }, StjWire.Mvc());
            var result = conformance.ValidateComponent("TimeSpanModel", wire);
            result.IsValid.Should().BeTrue($"{wire!.ToJsonString()}: {string.Join("; ", result.Errors)}");
        }
    }
}
