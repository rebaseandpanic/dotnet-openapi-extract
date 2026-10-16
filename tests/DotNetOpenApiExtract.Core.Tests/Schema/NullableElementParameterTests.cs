using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Elements and dictionary values of action parameters: a query parameter, a request body and a form
/// field have the nullable form of the version exactly at the positions their declaration annotates
/// <c>?</c>; the parameter itself, the key and the other positions keep their schemas.
/// </summary>
/// <remarks>
/// Places: <c>ModernApi.Controllers.NullableElementsController</c> (query, bodies, form) and
/// <c>LegacyNoNrtApi.Controllers.LegacyCollectionsController</c> (bodies); the expected positions are the
/// <c>?</c> of their declarations. Query and form use the forms the tool describes with a schema: a
/// parameter bound by an explicit <c>[FromQuery]</c> / <c>[FromForm]</c> is one parameter or field whose
/// schema is generated for its declared type. Nullable forms as in the field catalog (<c>schema.nullable</c>,
/// <c>schema.anyOf</c>): <c>nullable: true</c> in 3.0, <c>"null"</c> among the types in 3.1+; a reference
/// is <c>anyOf: [$ref, null branch]</c>, the null branch being
/// <c>{type: object, nullable: true, enum: [null]}</c> in 3.0 and <c>{type: "null"}</c> in 3.1+.
/// </remarks>
public class NullableElementParameterTests(NullableElementSchemaFixture fixture) : IClassFixture<NullableElementSchemaFixture>
{
    private const string ItemRef = "#/components/schemas/ElementItem";

    private static readonly DocumentFormat[] Formats = [DocumentFormat.Json, DocumentFormat.Yaml];

    /// <summary>A nullable string in 3.0 and in 3.1+.</summary>
    private static readonly (string V30, string V31) NullableString =
        ("""{"type": "string", "nullable": true}""", """{"type": ["string", "null"]}""");

    /// <summary>A nullable reference to the element component in 3.0 and in 3.1+.</summary>
    private static readonly (string V30, string V31) NullableItem = (
        $$"""{"anyOf": [{"$ref": "{{ItemRef}}"}, {"type": "object", "nullable": true, "enum": [null]}]}""",
        $$"""{"anyOf": [{"$ref": "{{ItemRef}}"}, {"type": "null"}]}""");

    /// <summary>A nullable array of <paramref name="items"/> in 3.0 and in 3.1+.</summary>
    private static (string V30, string V31) NullableArrayOf((string V30, string V31) items) => (
        $$"""{"type": "array", "nullable": true, "items": {{items.V30}}}""",
        $$"""{"type": ["array", "null"], "items": {{items.V31}}}""");

    /// <summary>
    /// A place of an operation and its whole schema per version: the parameter, the body or the field
    /// itself is never nullable, its annotated positions are.
    /// </summary>
    private sealed record Place(string Path, string Method, string Locator, string Expected30, string Expected31);

    private static Place Of(string path, string method, string locator, string kind, string inner, (string V30, string V31) element) =>
        new(path, method, locator,
            $$"""{"type": "{{kind}}", "{{inner}}": {{element.V30}}}""",
            $$"""{"type": "{{kind}}", "{{inner}}": {{element.V31}}}""");

    private static readonly Place[] Places =
    [
        // (8) the query collection; (18) the nested generic and the dictionary bound by [FromQuery].
        Of("/nullable-elements/query", "get", "parameter:tags", "array", "items", NullableString),
        Of("/nullable-elements/query", "get", "parameter:nested", "array", "items", NullableArrayOf(NullableString)),
        Of("/nullable-elements/query", "get", "parameter:map", "object", "additionalProperties", NullableString),

        // (9) and (17): bodies.
        Of("/nullable-elements/body", "post", "body", "array", "items", NullableItem),
        Of("/nullable-elements/body-dictionary", "post", "body", "object", "additionalProperties", NullableItem),
        Of("/nullable-elements/body-nested", "post", "body", "array", "items", NullableArrayOf(NullableItem)),

        // (10) the form collection; (18) the nested generic and the dictionary bound by [FromForm].
        Of("/nullable-elements/form", "post", "form:names", "array", "items", NullableString),
        Of("/nullable-elements/form", "post", "form:nested", "array", "items", NullableArrayOf(NullableString)),
        Of("/nullable-elements/form", "post", "form:map", "object", "additionalProperties", NullableItem),
    ];

    public static TheoryData<OpenApiSpecVersion, DocumentFormat, int> PlaceCases()
    {
        var data = new TheoryData<OpenApiSpecVersion, DocumentFormat, int>();
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
        for (var i = 0; i < Places.Length; i++)
            data.Add(version, format, i);
        return data;
    }

    private static JsonNode Schema(JsonNode document, string path, string method, string locator)
    {
        var operation = document["paths"]![path]![method]!;
        var (kind, name) = locator.Split(':') is [var k, var n] ? (k, n) : (locator, "");
        return kind switch
        {
            "parameter" => operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!["schema"]!,
            "body" => operation["requestBody"]!["content"]!["application/json"]!["schema"]!,
            "form" => operation["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!["properties"]![name]!,
            _ => throw new ArgumentException(locator),
        };
    }

    /// <summary>The JSON with every <c>type</c> list sorted, the order of types carrying no meaning.</summary>
    private static JsonNode? Normalized(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(kv => KeyValuePair.Create(kv.Key,
            kv.Key == "type" && kv.Value is JsonArray types
                ? (JsonNode?)new JsonArray(types.Select(t => t!.GetValue<string>()).Order(StringComparer.Ordinal).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray())
                : Normalized(kv.Value)))),
        JsonArray a => new JsonArray(a.Select(Normalized).ToArray()),
        _ => node?.DeepClone(),
    };

    [Theory]
    [MemberData(nameof(PlaceCases))]
    public void ParameterBodyAndFieldPositions_HaveTheNullableFormOfTheVersion_ExactlyWhereAnnotated(
        OpenApiSpecVersion version, DocumentFormat format, int index)
    {
        var place = Places[index];
        var expected = JsonNode.Parse(version == OpenApiSpecVersion.OpenApi3_0 ? place.Expected30 : place.Expected31);
        var actual = Schema(fixture.Modern[(version, format)], place.Path, place.Method, place.Locator);

        // The whole schema of the place: the place itself without nullable, no propertyNames for a
        // string key, the nullable form exactly at the annotated positions.
        JsonNode.DeepEquals(Normalized(actual), Normalized(expected)).Should().BeTrue(
            $"{place.Path} {place.Locator} must be {expected!.ToJsonString()}, got {actual.ToJsonString()}");
    }

    // --- The assembly without NRT (kind 19), bodies ------------------------------------------------------

    public static TheoryData<string, string> LegacyBodies() => new()
    {
        { "/legacy/body-strings", "/items" },
        { "/legacy/body-dictionary", "/additionalProperties" },
        { "/legacy/body-nested", "/items" },
        { "/legacy/body-nested", "/items/items" },
        { "/legacy/body-array", "/items" },
    };

    [Theory]
    [MemberData(nameof(LegacyBodies))]
    public void BodiesOfAnAssemblyWithoutNrt_HaveNoNullableElementsOrValues(string path, string pointer)
    {
        NullableElementSchemaFixture.LegacyAssemblyHasNoNullableMetadata();

        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
        {
            JsonNode? position = Schema(fixture.Legacy[(version, format)], path, "post", "body");
            foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
                position = position?[segment];
            var because = $"{path}{pointer} {version} {format}";

            position.Should().NotBeNull(because);
            position!.AsObject().ContainsKey("nullable").Should().BeFalse(because);
            position.AsObject().ContainsKey("anyOf").Should().BeFalse(because);
            (position["type"]?.ToJsonString() ?? string.Empty).Should().NotContain("null", because);
        }
    }

    // --- No diagnostics -----------------------------------------------------------------------------

    [Fact]
    public void ThePlaces_GetNoDiagnostics()
    {
        string[] operations = ["query", "body", "body-dictionary", "body-nested", "form"];
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            fixture.Diagnostics[version]
                .Where(d => d.Location is { } location
                    && operations.Any(o => location.StartsWith($"#/paths/~1nullable-elements~1{o}/", StringComparison.Ordinal)
                        || location.EndsWith($" /nullable-elements/{o}", StringComparison.Ordinal)))
                .Should().BeEmpty($"{version}");
        }
    }
}
