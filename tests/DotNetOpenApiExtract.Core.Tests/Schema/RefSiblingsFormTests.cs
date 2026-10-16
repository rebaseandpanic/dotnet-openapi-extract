using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi built once per version and serialized to JSON and YAML.</summary>
public sealed class RefSiblingsFormFixture
{
    public RefSiblingsFormFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = VersionedDocumentHarness.Build(VersionedDocumentHarness.ModernApiOptions(version));
            foreach (var format in new[] { DocumentFormat.Json, DocumentFormat.Yaml })
                Documents[(version, format)] = AsJson(VersionedDocumentHarness
                    .SerializeAsync(document, version, format, CancellationToken.None).GetAwaiter().GetResult());
        }
    }

    /// <summary>
    /// The parsed document with JSON values: the YAML reading gives numbers as decimals and a YAML
    /// <c>null</c> as Microsoft.OpenApi's null sentinel string.
    /// </summary>
    private static JsonNode AsJson(JsonNode node) => JsonNode.Parse(WithNulls(node)?.ToJsonString() ?? "null")!;

    private static JsonNode? WithNulls(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                    o[key] = WithNulls(o[key]?.DeepClone());
                return o;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                    a[i] = WithNulls(a[i]?.DeepClone());
                return a;
            default:
                return node is not null && JsonNullSentinel.IsJsonNullSentinel(node) ? null : node;
        }
    }

    public Dictionary<(OpenApiSpecVersion Version, DocumentFormat Format), JsonNode> Documents { get; } = [];
}

/// <summary>
/// The schema of a DTO property that references a component and carries keywords of its own: in 3.1
/// and 3.2 the keywords stand next to <c>$ref</c> with the values of the 3.0 <c>allOf</c> wrapper, which
/// 3.0 keeps; a nullable reference is an <c>anyOf</c> with the keywords beside it; <c>type</c> never
/// stands next to <c>$ref</c>; a reference without keywords stays <c>{$ref}</c> and the referenced
/// component never takes a keyword of a place.
/// </summary>
/// <remarks>
/// Expected values are the literals of <c>ModernApi.Models.Keywords.RefSiblingsModel</c>; the forms are
/// those of the field catalog (example: <c>example</c> in 3.0, <c>examples: [v]</c> in 3.1+; one allowed
/// string: a one-element <c>enum</c> in 3.0, <c>const</c> in 3.1+), and the null branch of a nullable
/// reference by version (catalog, <c>schema.anyOf</c>): <c>{type: object, nullable: true, enum: [null]}</c>
/// in 3.0, <c>{type: "null"}</c> in 3.1+.
/// </remarks>
public class RefSiblingsFormTests(RefSiblingsFormFixture fixture) : IClassFixture<RefSiblingsFormFixture>
{
    private const string TargetRef = "#/components/schemas/SiblingTarget";

    private static readonly DocumentFormat[] Formats = [DocumentFormat.Json, DocumentFormat.Yaml];

    /// <summary>Every version × format.</summary>
    public static TheoryData<OpenApiSpecVersion, DocumentFormat> AllForms()
    {
        var data = new TheoryData<OpenApiSpecVersion, DocumentFormat>();
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
            data.Add(version, format);
        return data;
    }

    /// <summary>
    /// One class of sibling keyword per property, with the keywords the place carries (a JSON object of
    /// the declared values), × every version and format.
    /// </summary>
    public static TheoryData<OpenApiSpecVersion, DocumentFormat, string, string> SiblingClasses()
    {
        var data = new TheoryData<OpenApiSpecVersion, DocumentFormat, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
        {
            var example = version == OpenApiSpecVersion.OpenApi3_0
                ? """{"example": {"name": "place-example"}}"""
                : """{"examples": [{"name": "place-example"}]}""";
            foreach (var (property, keywords) in new[]
            {
                ("described",       """{"description": "place-description"}"""),
                ("titled",          """{"title": "place-title"}"""),
                ("defaulted",       """{"default": "place-default"}"""),
                ("exemplified",     example),
                ("obsoleted",       """{"deprecated": true}"""),
                ("readOnlyTarget",  """{"readOnly": true}"""),
                ("writeOnlyTarget", """{"writeOnly": true}"""),
                ("lengths",         """{"minLength": 1, "maxLength": 5}"""),
                ("patterned",       """{"pattern": "^place$"}"""),
                ("formatted",       """{"format": "place-format"}"""),
                ("xmlDescribed",    """{"description": "xml-description"}"""),
                ("xmlOverAttribute", """{"description": "xml-wins"}"""),
            })
                data.Add(version, format, property, keywords);
        }

        return data;
    }

    private JsonNode Document(OpenApiSpecVersion version, DocumentFormat format) => fixture.Documents[(version, format)];

    private JsonObject Property(OpenApiSpecVersion version, DocumentFormat format, string name, string model = "RefSiblingsModel") =>
        Document(version, format)["components"]!["schemas"]![model]!["properties"]![name]!.AsObject();

    private static bool Is30(OpenApiSpecVersion version) => version == OpenApiSpecVersion.OpenApi3_0;

    private static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    private static void ShouldBe(JsonNode? actual, string expectedJson) =>
        JsonNode.DeepEquals(actual, Parse(expectedJson)).Should().BeTrue(
            $"expected {expectedJson}, got {actual?.ToJsonString() ?? "nothing"}");

    private static IEnumerable<string> Keys(JsonObject node) => node.Select(kv => kv.Key);

    [Theory]
    [MemberData(nameof(SiblingClasses))]
    public void Keywords_StandNextToRef_From31_AndOnTheAllOfWrapperIn30(
        OpenApiSpecVersion version, DocumentFormat format, string property, string keywordsJson)
    {
        var place = Property(version, format, property);
        var keywords = Parse(keywordsJson).AsObject();

        var form = Is30(version) ? "allOf" : "$ref";
        Keys(place).Should().BeEquivalentTo(Keys(keywords).Append(form), "the place carries exactly the declared keywords");
        foreach (var (keyword, value) in keywords)
            JsonNode.DeepEquals(place[keyword], value).Should().BeTrue(
                $"{property}.{keyword} must be {value?.ToJsonString()}, got {place[keyword]?.ToJsonString() ?? "nothing"}");

        if (Is30(version))
            ShouldBe(place["allOf"], $$"""[{"$ref": "{{TargetRef}}"}]""");
        else
            place["$ref"]!.GetValue<string>().Should().Be(TargetRef);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public void AllowedValues_OnAReference_StandNextToRef_From31_AndAreAnAllOfElementIn30(
        OpenApiSpecVersion version, DocumentFormat format)
    {
        var allowed = Property(version, format, "allowed");
        var single = Property(version, format, "allowedSingle");

        if (Is30(version))
        {
            Keys(allowed).Should().BeEquivalentTo(["allOf", "not"]);
            ShouldBe(allowed["allOf"], $$"""[{"$ref": "{{TargetRef}}"}, {"enum": ["low", "high"]}]""");
            ShouldBe(allowed["not"], """{"enum": ["mid"]}""");

            Keys(single).Should().BeEquivalentTo(["allOf"]);
            ShouldBe(single["allOf"], $$"""[{"$ref": "{{TargetRef}}"}, {"enum": ["low"]}]""");
            return;
        }

        Keys(allowed).Should().BeEquivalentTo(["$ref", "enum", "not"], "the constraint is no longer an element of allOf");
        allowed["$ref"]!.GetValue<string>().Should().Be(TargetRef);
        ShouldBe(allowed["enum"], """["low", "high"]""");
        ShouldBe(allowed["not"], """{"enum": ["mid"]}""");

        Keys(single).Should().BeEquivalentTo(["$ref", "const"]);
        single["$ref"]!.GetValue<string>().Should().Be(TargetRef);
        single["const"]!.GetValue<string>().Should().Be("low");
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public void NullableReference_IsAnAnyOf_WithItsKeywordsBesideIt(OpenApiSpecVersion version, DocumentFormat format)
    {
        var nullBranch = Is30(version)
            ? """{"type": "object", "nullable": true, "enum": [null]}"""
            : """{"type": "null"}""";
        var anyOf = $$"""[{"$ref": "{{TargetRef}}"}, {{nullBranch}}]""";

        var described = Property(version, format, "nullableDescribed");
        Keys(described).Should().BeEquivalentTo(["anyOf", "description", "maxLength"]);
        ShouldBe(described["anyOf"], anyOf);
        described["description"]!.GetValue<string>().Should().Be("nullable-description");
        described["maxLength"]!.GetValue<int>().Should().Be(3);

        var allowed = Property(version, format, "nullableAllowed");
        var single = Property(version, format, "nullableAllowedSingle");
        if (Is30(version))
        {
            // The 3.0 form is unchanged: an outer allOf around the anyOf and the allowed values.
            allowed["allOf"]!.AsArray().Should().HaveCount(2);
            ShouldBe(allowed["allOf"]![0]!["anyOf"], anyOf);
            ShouldBe(allowed["allOf"]![1], """{"enum": ["low", "high"]}""");
            ShouldBe(allowed["not"], """{"enum": ["mid"]}""");

            single["allOf"]!.AsArray().Should().HaveCount(2);
            ShouldBe(single["allOf"]![0]!["anyOf"], anyOf);
            ShouldBe(single["allOf"]![1], """{"enum": ["low"]}""");
            return;
        }

        Keys(allowed).Should().BeEquivalentTo(["anyOf", "description", "enum", "not"], "no outer allOf in 3.1+");
        ShouldBe(allowed["anyOf"], anyOf);
        allowed["description"]!.GetValue<string>().Should().Be("nullable-allowed");
        ShouldBe(allowed["enum"], """["low", "high"]""");
        ShouldBe(allowed["not"], """{"enum": ["mid"]}""");

        Keys(single).Should().BeEquivalentTo(["anyOf", "const"]);
        ShouldBe(single["anyOf"], anyOf);
        single["const"]!.GetValue<string>().Should().Be("low");
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public void Ref_NeverHasATypeBesideIt(OpenApiSpecVersion version, DocumentFormat format)
    {
        var places = Document(version, format)["components"]!["schemas"]!["RefSiblingsModel"]!["properties"]!.AsObject();
        places.Should().NotBeEmpty();

        var refs = Descendants(places).Where(n => n.ContainsKey("$ref")).ToList();
        refs.Should().NotBeEmpty();
        refs.Should().AllSatisfy(r => r.ContainsKey("type").Should().BeFalse($"{r.ToJsonString()} has a type next to $ref"));
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public void ReferenceWithoutKeywords_IsABareRef_AndTheTargetTakesNoKeywordOfAPlace(
        OpenApiSpecVersion version, DocumentFormat format)
    {
        ShouldBe(Property(version, format, "bare"), $$"""{"$ref": "{{TargetRef}}"}""");
        ShouldBe(Property(version, format, "target", "RefSiblingsNeighbour"), $$"""{"$ref": "{{TargetRef}}"}""");

        // SiblingTarget declares an object with one string property and its XML summary.
        var target = Document(version, format)["components"]!["schemas"]!["SiblingTarget"]!.AsObject();
        target["description"]!.GetValue<string>().Should().Be("Sibling target component.");
        Keys(target).Should().NotContain(
            ["title", "default", "example", "examples", "deprecated", "readOnly", "writeOnly", "minLength", "maxLength",
             "pattern", "format", "enum", "const", "not", "anyOf", "allOf"],
            "keywords of the places referencing the component stay in the places");
        Keys(target).Should().NotContain(k => k.StartsWith("x-"));
    }

    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
                yield return o;
                foreach (var (_, child) in o)
                    if (child != null)
                        foreach (var d in Descendants(child))
                            yield return d;
                break;
            case JsonArray a:
                foreach (var child in a)
                    if (child != null)
                        foreach (var d in Descendants(child))
                            yield return d;
                break;
        }
    }
}
