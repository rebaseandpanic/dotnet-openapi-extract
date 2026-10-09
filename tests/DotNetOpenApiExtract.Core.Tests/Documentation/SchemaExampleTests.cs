using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Documentation;

/// <summary>ModernApi for every version: parsed document and diagnostics.</summary>
public sealed class SchemaExampleFixture
{
    public SchemaExampleFixture()
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
            Documents[version] = JsonNode.Parse(
                document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;
            Diagnostics[version] = diagnostics;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];
}

/// <summary>
/// XML <c>&lt;example&gt;</c> on DTO properties and types: parsed by the schema, written as
/// <c>example</c> for 3.0 and <c>examples: [v]</c> for 3.1+; on a reference's <c>allOf</c> wrapper
/// and on the numeric branch of a number union; values that do not parse and repeated examples are
/// reported and never written as text.
/// </summary>
public class SchemaExampleTests(SchemaExampleFixture fixture) : IClassFixture<SchemaExampleFixture>
{
    private const string ExampleNotParsable = ExtractionDiagnosticCodes.SchemaExampleNotParsable;
    private const string ExampleMultiple = ExtractionDiagnosticCodes.SchemaExampleMultiple;

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Component(OpenApiSpecVersion version, string id) =>
        fixture.Documents[version]["components"]!["schemas"]![id]!.AsObject();

    private JsonObject Property(OpenApiSpecVersion version, string model, string name) =>
        Component(version, model)["properties"]![name]!.AsObject();

    /// <summary>
    /// The example of <paramref name="schema"/> in the version's form, as JSON text ("null" for a JSON
    /// null); <see langword="null"/> when the schema has none. The other form must be absent.
    /// </summary>
    private static string? Example(JsonObject schema, OpenApiSpecVersion version)
    {
        if (version == OpenApiSpecVersion.OpenApi3_0)
        {
            schema.ContainsKey("examples").Should().BeFalse(because: "3.0 has no schema examples");
            return schema.ContainsKey("example") ? schema["example"]?.ToJsonString() ?? "null" : null;
        }

        schema.ContainsKey("example").Should().BeFalse(because: "3.1+ writes examples, example is deprecated");
        if (!schema.ContainsKey("examples"))
            return null;
        var examples = schema["examples"]!.AsArray();
        examples.Should().ContainSingle();
        return examples[0]?.ToJsonString() ?? "null";
    }

    private static string Json(string text) => JsonNode.Parse(text)?.ToJsonString() ?? "null";

    public static TheoryData<OpenApiSpecVersion, string, string> PropertyExamples()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "count", "42");
            data.Add(version, "big", "9007199254740993");
            data.Add(version, "ratio", "1.5");
            data.Add(version, "enabled", "true");
            data.Add(version, "code", "\"123\"");
            data.Add(version, "day", "\"2024-01-02\"");
            data.Add(version, "map", "{\"a\": 1}");
            data.Add(version, "items", "[1, 2]");
            data.Add(version, "note", "null");
            data.Add(version, "maybeCount", "null");
            data.Add(version, "withoutSummary", "7");
            data.Add(version, "inherited", "17");
            data.Add(version, "tint", "\"ruby\"");
            data.Add(version, "spaced", "\"two  spaces\"");
            data.Add(version, "spacedObject", "{\"text\": \"a  b\"}");
            data.Add(version, "multiLine", "{\"a\": 1}");
            data.Add(version, "padded", "\"  padded  \"");
            data.Add(version, "blank", "\"   \"");
            data.Add(version, "lines", "\"first line\\n  second line\"");
            data.Add(version, "cdata", "\" <tag>  x \"");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PropertyExamples))]
    public void PropertyExample_IsParsedByTheSchema_InTheVersionForm(OpenApiSpecVersion version, string property, string expected)
    {
        Example(Property(version, "ExampleModel", property), version).Should().Be(Json(expected));
        fixture.Diagnostics[version].Should().NotContain(d => d.Location == $"#/components/schemas/ExampleModel/properties/{property}");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void TypeAndRecordExamples_AreWritten(OpenApiSpecVersion version)
    {
        Example(Component(version, "ExampleModel"), version).Should().Be(Json("{\"count\": 3, \"code\": \"abc\"}"));
        Example(Property(version, "ExampleRecord", "amount"), version).Should().Be("250");
        Example(Property(version, "ExampleRecord", "currency"), version).Should().Be("\"EUR\"");
        fixture.Diagnostics[version].Should().NotContain(d =>
            d.Location != null && (d.Location.StartsWith("#/components/schemas/ExampleModel", StringComparison.Ordinal)
                                   || d.Location.StartsWith("#/components/schemas/ExampleRecord", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ReferenceExample_IsOnTheWrapper_AndTheComponentKeepsItsOwn(OpenApiSpecVersion version)
    {
        var wrapper = Property(version, "ExampleModel", "target");

        wrapper["allOf"]![0]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ExampleTarget");
        Example(wrapper, version).Should().Be(Json("{\"label\": \"from the property\"}"));
        wrapper["description"]!.GetValue<string>().Should().Be("A reference.");
        Example(Component(version, "ExampleTarget"), version).Should().Be(Json("{\"label\": \"from the type\"}"));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NumberUnionExample_IsOnTheNumericBranchOnly(OpenApiSpecVersion version)
    {
        var union = Property(version, "ExampleModel", "lenient");
        var branches = union["anyOf"]!.AsArray().Select(b => b!.AsObject()).ToList();

        Example(union, version).Should().BeNull();
        Example(branches[0], version).Should().Be("5");
        branches.Skip(1).Should().OnlyContain(b => Example(b, version) == null);
    }

    public static TheoryData<OpenApiSpecVersion, string, string> Unparsable()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "notANumber", "many");
            data.Add(version, "fraction", "1.5");
            data.Add(version, "notNullable", "null");
            data.Add(version, "notNullableText", "null");
            data.Add(version, "wrongKind", "{\"a\": 1}");
            data.Add(version, "broken", "[1,");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Unparsable))]
    public void UnparsableExample_IsNotWritten_OneWarningNamingElementAndValue(OpenApiSpecVersion version, string property, string text)
    {
        var location = $"#/components/schemas/ExampleFailuresModel/properties/{property}";
        var element = $"ModernApi.Models.Keywords.ExampleFailuresModel.{char.ToUpperInvariant(property[0])}{property[1..]}";

        Example(Property(version, "ExampleFailuresModel", property), version).Should().BeNull();
        var warning = fixture.Diagnostics[version].Where(d => d.Location == location).Should().ContainSingle().Which;
        warning.Code.Should().Be(ExampleNotParsable);
        warning.Subjects.Should().Equal(element, text);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SeveralExamples_TheFirstIsUsed_OneWarning(OpenApiSpecVersion version)
    {
        const string twice = "#/components/schemas/ExampleFailuresModel/properties/twice";
        const string twiceBroken = "#/components/schemas/ExampleFailuresModel/properties/twiceBroken";

        Example(Property(version, "ExampleFailuresModel", "twice"), version).Should().Be("1");
        var warning = fixture.Diagnostics[version].Where(d => d.Location == twice).Should().ContainSingle().Which;
        warning.Code.Should().Be(ExampleMultiple);
        warning.Subjects.Should().Equal("ModernApi.Models.Keywords.ExampleFailuresModel.Twice");

        Example(Property(version, "ExampleFailuresModel", "twiceBroken"), version).Should().BeNull();
        fixture.Diagnostics[version].Where(d => d.Location == twiceBroken).Select(d => d.Code)
            .Should().BeEquivalentTo([ExampleMultiple, ExampleNotParsable]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void PolymorphicBaseExample_IsOnTheUnion_NotOnItsVariants(OpenApiSpecVersion version)
    {
        var union = Component(version, "ExampleShape");
        union.ContainsKey("oneOf").Should().BeTrue(because: "the base is a union component");
        Example(union, version).Should().Be(Json("{\"kind\": \"circle\", \"radius\": 2}"));
        Example(Component(version, "ExampleCircleAsExampleShape"), version).Should().BeNull();
        fixture.Diagnostics[version].Should().NotContain(d => d.Location == "#/components/schemas/ExampleShape");

        Example(Component(version, "ExampleBrokenShape"), version).Should().BeNull();
        fixture.Diagnostics[version].Where(d => d.Location == "#/components/schemas/ExampleBrokenShape").Select(d => d.Code)
            .Should().BeEquivalentTo([ExampleMultiple, ExampleNotParsable]);
    }

    public static TheoryData<OpenApiSpecVersion, string, string> NonObjectUnionExamples()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "NumberExampleShape", "42");
            data.Add(version, "BooleanExampleShape", "true");
            data.Add(version, "StringExampleShape", "\"x\"");
            data.Add(version, "ArrayExampleShape", "[]");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(NonObjectUnionExamples))]
    public void PolymorphicBaseExample_ThatIsNotAnObject_IsNotWritten_OneWarning(OpenApiSpecVersion version, string union, string text)
    {
        var location = $"#/components/schemas/{union}";

        Component(version, union).ContainsKey("oneOf").Should().BeTrue();
        Example(Component(version, union), version).Should().BeNull();
        var warning = fixture.Diagnostics[version].Where(d => d.Location == location).Should().ContainSingle().Which;
        warning.Code.Should().Be(ExampleNotParsable);
        warning.Subjects.Should().Equal($"ModernApi.Models.Keywords.{union}", text);
    }
}
