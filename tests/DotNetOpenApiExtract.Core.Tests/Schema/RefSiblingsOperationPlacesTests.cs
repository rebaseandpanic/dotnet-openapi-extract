using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// The schema of a path, query or header parameter, a request body and a form field that references
/// a component and carries keywords of its own: the 3.0 <c>allOf</c> wrapper stays, in 3.1 and 3.2 the
/// keywords stand next to <c>$ref</c>, a parameter's default among them; a nullable reference with
/// allowed values is an <c>anyOf</c> with the values beside it, without an outer <c>allOf</c>.
/// </summary>
/// <remarks>
/// Places: <c>ModernApi.Controllers.RefSiblingsOperationsController</c>; expected values are its
/// attribute literals, in the forms of the field catalog (one allowed string: a one-element
/// <c>enum</c> in 3.0, <c>const</c> in 3.1+) and the null branches of ИНВ-1.
/// </remarks>
public class RefSiblingsOperationPlacesTests(RefSiblingsFormFixture fixture) : IClassFixture<RefSiblingsFormFixture>
{
    private const string Target = """{"$ref": "#/components/schemas/SiblingTarget"}""";
    private const string Point = """{"$ref": "#/components/schemas/SiblingPoint"}""";
    private const string Null30 = """{"type": "object", "nullable": true, "enum": [null]}""";
    private const string Null31 = """{"type": "null"}""";

    private static readonly DocumentFormat[] Formats = [DocumentFormat.Json, DocumentFormat.Yaml];

    /// <summary>A place of an operation: where its schema is, and its 3.0 and 3.1+ forms.</summary>
    private sealed record Place(string Path, string Method, string Locator, string Expected30, string Expected31);

    private static readonly Place[] Places =
    [
        // Query parameters: one class of keyword each.
        new("/ref-siblings-ops/query", "get", "parameter:lengths",
            $$$"""{"allOf": [{{{Target}}}], "minLength": 1, "maxLength": 5}""",
            $$$"""{"$ref": "#/components/schemas/SiblingTarget", "minLength": 1, "maxLength": 5}"""),
        new("/ref-siblings-ops/query", "get", "parameter:patterned",
            $$$"""{"allOf": [{{{Target}}}], "pattern": "^place$"}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "pattern": "^place$"}"""),
        new("/ref-siblings-ops/query", "get", "parameter:formatted",
            $$$"""{"allOf": [{{{Target}}}], "format": "email"}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "format": "email"}"""),
        new("/ref-siblings-ops/query", "get", "parameter:allowed",
            $$$"""{"allOf": [{{{Target}}}, {"enum": ["low", "high"]}], "not": {"enum": ["mid"]}}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "enum": ["low", "high"], "not": {"enum": ["mid"]}}"""),
        // default × a reference parameter with a constraint.
        new("/ref-siblings-ops/query", "get", "parameter:defaulted",
            $$$"""{"allOf": [{{{Target}}}, {"enum": ["low"]}], "default": "low"}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "const": "low", "default": "low"}"""),
        new("/ref-siblings-ops/path/{target}", "get", "parameter:target",
            $$$"""{"allOf": [{{{Target}}}], "maxLength": 5}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "maxLength": 5}"""),
        new("/ref-siblings-ops/header", "get", "parameter:X-Target",
            $$$"""{"allOf": [{{{Target}}}], "pattern": "^place$"}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "pattern": "^place$"}"""),

        // Request bodies.
        new("/ref-siblings-ops/body", "post", "body",
            $$$"""{"allOf": [{{{Target}}}], "minLength": 1, "maxLength": 5}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "minLength": 1, "maxLength": 5}"""),
        new("/ref-siblings-ops/body-allowed", "post", "body",
            $$$"""{"allOf": [{{{Target}}}, {"enum": ["low"]}], "not": {"enum": ["mid"]}}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "const": "low", "not": {"enum": ["mid"]}}"""),

        // Form fields.
        new("/ref-siblings-ops/form", "post", "form:field",
            $$$"""{"allOf": [{{{Target}}}], "pattern": "^place$", "maxLength": 5}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "pattern": "^place$", "maxLength": 5}"""),
        new("/ref-siblings-ops/form", "post", "form:allowedField",
            $$$"""{"allOf": [{{{Target}}}, {"enum": ["low"]}]}""",
            """{"$ref": "#/components/schemas/SiblingTarget", "const": "low"}"""),
    ];

    /// <summary>Nullable references with allowed and denied values (P-NULLC) on each kind of place.</summary>
    private static readonly Place[] NullablePlaces =
    [
        new("/ref-siblings-ops/query", "get", "parameter:nullableAllowed",
            $$$"""{"allOf": [{"anyOf": [{{{Point}}}, {{{Null30}}}]}, {"enum": ["low", "high"]}], "not": {"enum": ["mid"]}}""",
            $$$"""{"anyOf": [{{{Point}}}, {{{Null31}}}], "enum": ["low", "high"], "not": {"enum": ["mid"]}}"""),
        new("/ref-siblings-ops/body-nullable", "post", "body",
            $$$"""{"allOf": [{"anyOf": [{{{Point}}}, {{{Null30}}}]}, {"enum": ["low", "high"]}], "not": {"enum": ["mid"]}}""",
            $$$"""{"anyOf": [{{{Point}}}, {{{Null31}}}], "enum": ["low", "high"], "not": {"enum": ["mid"]}}"""),
        new("/ref-siblings-ops/form", "post", "form:nullableField",
            $$$"""{"allOf": [{"anyOf": [{{{Point}}}, {{{Null30}}}]}, {"enum": ["low", "high"]}], "not": {"enum": ["mid"]}}""",
            $$$"""{"anyOf": [{{{Point}}}, {{{Null31}}}], "enum": ["low", "high"], "not": {"enum": ["mid"]}}"""),
    ];

    private static TheoryData<OpenApiSpecVersion, DocumentFormat, int> Cases(int count)
    {
        var data = new TheoryData<OpenApiSpecVersion, DocumentFormat, int>();
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
        for (var i = 0; i < count; i++)
            data.Add(version, format, i);
        return data;
    }

    public static TheoryData<OpenApiSpecVersion, DocumentFormat, int> PlaceCases() => Cases(Places.Length);

    public static TheoryData<OpenApiSpecVersion, DocumentFormat, int> NullablePlaceCases() => Cases(NullablePlaces.Length);

    private JsonNode Schema(OpenApiSpecVersion version, DocumentFormat format, Place place)
    {
        var operation = fixture.Documents[(version, format)]["paths"]![place.Path]![place.Method]!;
        var (kind, name) = place.Locator.Split(':') is [var k, var n] ? (k, n) : (place.Locator, "");
        return kind switch
        {
            "parameter" => operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!["schema"]!,
            "body" => operation["requestBody"]!["content"]!["application/json"]!["schema"]!,
            "form" => operation["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!["properties"]![name]!,
            _ => throw new ArgumentException(place.Locator),
        };
    }

    private void AssertPlace(OpenApiSpecVersion version, DocumentFormat format, Place place)
    {
        var expected = JsonNode.Parse(version == OpenApiSpecVersion.OpenApi3_0 ? place.Expected30 : place.Expected31);
        var actual = Schema(version, format, place);
        JsonNode.DeepEquals(actual, expected).Should().BeTrue(
            $"{place.Path} {place.Locator} must be {expected!.ToJsonString()}, got {actual.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(PlaceCases))]
    public void Keywords_StandNextToRef_From31_AndOnTheAllOfWrapperIn30(OpenApiSpecVersion version, DocumentFormat format, int place) =>
        AssertPlace(version, format, Places[place]);

    [Theory]
    [MemberData(nameof(NullablePlaceCases))]
    public void NullableReferenceWithAllowedValues_IsAnAnyOfWithTheValuesBesideIt_From31(
        OpenApiSpecVersion version, DocumentFormat format, int place)
    {
        AssertPlace(version, format, NullablePlaces[place]);

        // The reference inside the anyOf never carries a type.
        var anyOf = version == OpenApiSpecVersion.OpenApi3_0
            ? Schema(version, format, NullablePlaces[place])["allOf"]![0]!["anyOf"]!
            : Schema(version, format, NullablePlaces[place])["anyOf"]!;
        anyOf[0]!.AsObject().ContainsKey("type").Should().BeFalse();
    }
}
