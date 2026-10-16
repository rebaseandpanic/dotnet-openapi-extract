using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.Schema;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Operations;

/// <summary>
/// Builds, for every version: ModernApi with its diagnostics, ModernApi under a global
/// <c>[Produces]</c> filter with two media types, and LegacyNoNrtApi.
/// </summary>
public sealed class NullableElementResponseFixture
{
    /// <summary>The media types of the global <c>[Produces]</c> filter.</summary>
    public static readonly string[] GlobalMediaTypes = ["application/json", "application/xml"];

    public NullableElementResponseFixture()
    {
        using var globalProduces = new TempDirectory();
        File.WriteAllText(
            Path.Combine(globalProduces.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute("application/json", "application/xml")));
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (modern, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Modern[version] = Serialize(modern, version);
            Diagnostics[version] = diagnostics;

            GlobalProduces[version] = Serialize(VersionedDocumentHarness.Build(new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                SourceRoot     = globalProduces.Path,
            }), version);

            Legacy[version] = Serialize(VersionedDocumentHarness.Build(VersionedDocumentHarness.LegacyNoNrtApiOptions(version)), version);
        }
    }

    private static JsonNode Serialize(OpenApiDocument document, OpenApiSpecVersion version) =>
        VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None).GetAwaiter().GetResult();

    public Dictionary<OpenApiSpecVersion, JsonNode> Modern { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];

    public Dictionary<OpenApiSpecVersion, JsonNode> GlobalProduces { get; } = [];

    public Dictionary<OpenApiSpecVersion, JsonNode> Legacy { get; } = [];
}

/// <summary>
/// A response body whose type comes from the action's signature has the nullable form of the version
/// exactly at the positions the signature annotates <c>?</c>, on every path the body type takes: the
/// return type (also through <c>Task&lt;&gt;</c> and <c>ActionResult&lt;&gt;</c>), a 200 declared without
/// a type, a typed result, each variant of <c>Results&lt;…&gt;</c> with its own type argument, and the
/// rebuild of the content for a global <c>[Produces]</c>. A body type given by an attribute has none.
/// </summary>
/// <remarks>
/// Places: <c>ModernApi.Controllers.NullableElementsController</c> (responses) and
/// <c>LegacyNoNrtApi.Controllers.LegacyCollectionsController</c> (responses of an assembly built without
/// nullable reference types); the expected positions are the <c>?</c> of the action declarations.
/// Nullable forms as in the field catalog (<c>schema.nullable</c>, <c>schema.anyOf</c>): a reference is
/// <c>anyOf: [$ref, null branch]</c>, the null branch being <c>{type: object, nullable: true, enum: [null]}</c>
/// in 3.0 and <c>{type: "null"}</c> in 3.1+; a nullable array has <c>nullable: true</c> in 3.0 and
/// <c>"null"</c> among its types in 3.1+.
/// </remarks>
public class NullableElementResponseTests(NullableElementResponseFixture fixture) : IClassFixture<NullableElementResponseFixture>
{
    private const string ItemRef = "#/components/schemas/ElementItem";
    private const string TypedItemRef = "#/components/schemas/TypedElementItem";

    /// <summary>A reference to <paramref name="reference"/> in 3.0 and in 3.1+, not nullable.</summary>
    private static (string V30, string V31) Ref(string reference) =>
        ($$"""{"$ref": "{{reference}}"}""", $$"""{"$ref": "{{reference}}"}""");

    /// <summary>A nullable reference to <paramref name="reference"/> in 3.0 and in 3.1+.</summary>
    private static (string V30, string V31) NullableRef(string reference) => (
        $$"""{"anyOf": [{"$ref": "{{reference}}"}, {"type": "object", "nullable": true, "enum": [null]}]}""",
        $$"""{"anyOf": [{"$ref": "{{reference}}"}, {"type": "null"}]}""");

    /// <summary>An array of <paramref name="items"/>.</summary>
    private static (string V30, string V31) ArrayOf((string V30, string V31) items) => (
        $$"""{"type": "array", "items": {{items.V30}}}""",
        $$"""{"type": "array", "items": {{items.V31}}}""");

    /// <summary>A nullable array of <paramref name="items"/>.</summary>
    private static (string V30, string V31) NullableArrayOf((string V30, string V31) items) => (
        $$"""{"type": "array", "nullable": true, "items": {{items.V30}}}""",
        $$"""{"type": ["array", "null"], "items": {{items.V31}}}""");

    /// <summary>An object whose values are <paramref name="values"/>.</summary>
    private static (string V30, string V31) MapOf((string V30, string V31) values) => (
        $$"""{"type": "object", "additionalProperties": {{values.V30}}}""",
        $$"""{"type": "object", "additionalProperties": {{values.V31}}}""");

    /// <summary>A response of an operation and the whole body schema per version.</summary>
    private sealed record Place(string Path, string Method, string Code, (string V30, string V31) Expected);

    private static readonly Place[] SignaturePlaces =
    [
        // The return type, also inside Task<> and ActionResult<>.
        new("/nullable-elements/response", "get", "200", ArrayOf(NullableRef(ItemRef))),
        new("/nullable-elements/response-task", "get", "200", ArrayOf(NullableRef(ItemRef))),
        new("/nullable-elements/response-action-result", "get", "200", ArrayOf(NullableRef(ItemRef))),

        // A 200 declared without a type takes the return type with its annotations.
        new("/nullable-elements/response-declared", "get", "200", ArrayOf(NullableRef(ItemRef))),

        // A typed result, and the first variant of a union.
        new("/nullable-elements/response-ok", "get", "200", ArrayOf(NullableRef(TypedItemRef))),
        new("/nullable-elements/response-union", "get", "200", ArrayOf(NullableRef(TypedItemRef))),

        // Two variants with the same CLR body type: each reads its own type argument.
        new("/nullable-elements/response-union-distinct", "post", "200", ArrayOf(Ref(TypedItemRef))),
        new("/nullable-elements/response-union-distinct", "post", "201", ArrayOf(NullableRef(TypedItemRef))),

        // The value of a dictionary, its key unchanged; a nested list nullable at both annotated levels.
        new("/nullable-elements/response-dictionary", "get", "200", MapOf(NullableRef(ItemRef))),
        new("/nullable-elements/response-nested", "get", "200", ArrayOf(NullableArrayOf(NullableRef(ItemRef)))),

        // A body type given by an attribute carries no annotations, whatever the signature says.
        new("/nullable-elements/response-attribute", "get", "200", ArrayOf(Ref(ItemRef))),
        new("/nullable-elements/response-swagger-attribute", "get", "200", ArrayOf(Ref(ItemRef))),
    ];

    public static TheoryData<OpenApiSpecVersion, int> SignatureCases()
    {
        var data = new TheoryData<OpenApiSpecVersion, int>();
        foreach (var version in VersionedDocumentHarness.Versions)
        for (var i = 0; i < SignaturePlaces.Length; i++)
            data.Add(version, i);
        return data;
    }

    private static JsonObject Content(JsonNode document, string path, string method, string code) =>
        document["paths"]![path]![method]!["responses"]![code]!["content"]!.AsObject();

    private static JsonNode Expected((string V30, string V31) expected, OpenApiSpecVersion version) =>
        JsonNode.Parse(version == OpenApiSpecVersion.OpenApi3_0 ? expected.V30 : expected.V31)!;

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

    private static void ShouldBe(JsonNode? actual, JsonNode expected, string because) =>
        JsonNode.DeepEquals(Normalized(actual), Normalized(expected)).Should().BeTrue(
            $"{because} must be {expected.ToJsonString()}, got {actual?.ToJsonString()}");

    [Theory]
    [MemberData(nameof(SignatureCases))]
    public void ResponseBody_HasTheNullableFormOfTheVersion_ExactlyWhereTheSignatureAnnotates(OpenApiSpecVersion version, int index)
    {
        var place = SignaturePlaces[index];
        var content = Content(fixture.Modern[version], place.Path, place.Method, place.Code);

        content.Select(kv => kv.Key).Should().BeEquivalentTo(["application/json"]);
        ShouldBe(content["application/json"]!["schema"], Expected(place.Expected, version), $"{place.Path} {place.Code} {version}");
    }

    // --- Content rebuilt for a global [Produces] ---------------------------------------------------

    private static readonly Place[] GlobalPlaces =
    [
        new("/nullable-elements/response", "get", "200", ArrayOf(NullableRef(ItemRef))),
        new("/nullable-elements/response-declared", "get", "200", ArrayOf(NullableRef(ItemRef))),
        new("/nullable-elements/response-dictionary", "get", "200", MapOf(NullableRef(ItemRef))),
    ];

    public static TheoryData<OpenApiSpecVersion, int> GlobalCases()
    {
        var data = new TheoryData<OpenApiSpecVersion, int>();
        foreach (var version in VersionedDocumentHarness.Versions)
        for (var i = 0; i < GlobalPlaces.Length; i++)
            data.Add(version, i);
        return data;
    }

    [Theory]
    [MemberData(nameof(GlobalCases))]
    public void ResponseBodyRebuiltForAGlobalProduces_KeepsTheNullableElementsInEveryMediaType(OpenApiSpecVersion version, int index)
    {
        var place = GlobalPlaces[index];
        var content = Content(fixture.GlobalProduces[version], place.Path, place.Method, place.Code);

        // The content is the global filter's: the rebuild ran.
        content.Select(kv => kv.Key).Should().BeEquivalentTo(NullableElementResponseFixture.GlobalMediaTypes);
        foreach (var mediaType in NullableElementResponseFixture.GlobalMediaTypes)
            ShouldBe(content[mediaType]!["schema"], Expected(place.Expected, version), $"{place.Path} {mediaType} {version}");
    }

    // --- Responses of the assembly without nullable reference types --------------------------------

    public static TheoryData<string, string> LegacyResponses() => new()
    {
        { "/legacy/response-strings", "/items" },
        { "/legacy/response-dictionary", "/additionalProperties" },
        { "/legacy/response-nested", "/items" },
        { "/legacy/response-nested", "/items/items" },
        { "/legacy/response-array", "/items" },
    };

    [Theory]
    [MemberData(nameof(LegacyResponses))]
    public void ResponsesOfAnAssemblyWithoutNrt_HaveNoNullableElementsOrValues(string path, string pointer)
    {
        NullableElementSchemaFixture.LegacyAssemblyHasNoNullableMetadata();

        foreach (var version in VersionedDocumentHarness.Versions)
        {
            JsonNode? position = Content(fixture.Legacy[version], path, "get", "200")["application/json"]!["schema"];
            foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
                position = position?[segment];
            var because = $"{path}{pointer} {version}";

            position.Should().NotBeNull(because);
            position!.AsObject().ContainsKey("nullable").Should().BeFalse(because);
            position.AsObject().ContainsKey("anyOf").Should().BeFalse(because);
            (position["type"]?.ToJsonString() ?? string.Empty).Should().NotContain("null", because);
        }
    }

    // --- No diagnostics -----------------------------------------------------------------------------

    [Fact]
    public void TheResponses_GetNoDiagnostics()
    {
        var operations = SignaturePlaces.Select(p => p.Path).Distinct().ToList();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            fixture.Diagnostics[version]
                .Where(d => d.Location is { } location
                    && operations.Any(path => location.StartsWith($"#/paths/{path.Replace("/", "~1")}/", StringComparison.Ordinal)
                        || location.EndsWith($" {path}", StringComparison.Ordinal)))
                .Should().BeEmpty($"{version}");
        }
    }
}
