using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using ModernApi.Models.NullableElements;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Conformance;

/// <summary>Builds ModernApi with its XML documentation for every version, serialized to JSON.</summary>
public sealed class NullableElementConformanceFixture
{
    public NullableElementConformanceFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = VersionedDocumentHarness.Build(new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
            });
            Documents[version] = VersionedDocumentHarness
                .SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (version != OpenApiSpecVersion.OpenApi3_0)
                Conformance[version] = SchemaConformance.For(Documents[version]);
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    /// <summary>The 2020-12 evaluator of the 3.1 and 3.2 documents.</summary>
    public Dictionary<OpenApiSpecVersion, SchemaConformance> Conformance { get; } = [];
}

/// <summary>
/// What System.Text.Json writes for collections, arrays and dictionaries with nullable elements and
/// values passes the schema of the place in 3.1 and 3.2: <c>null</c> in a position annotated <c>?</c>
/// is accepted, <c>null</c> in a position of the same place without <c>?</c> is rejected. The keywords
/// next to <c>$ref</c> keep the meaning of the 3.0 <c>allOf</c> wrapper.
/// </summary>
public class NullableElementConformanceTests(NullableElementConformanceFixture fixture)
    : IClassFixture<NullableElementConformanceFixture>
{
    public static TheoryData<OpenApiSpecVersion> SchemaVersions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    private static ElementItem Item => new() { Name = "item" };

    // ── Instances System.Text.Json writes, with null in each annotated position ────────────

    /// <summary>A DTO with <c>null</c> in every nullable element and value position.</summary>
    private static NullableElementsModel Elements() => new()
    {
        Strings = ["a", null],
        ItemArray = [Item, null],
        ItemSequence = [Item, null],
        StringSet = ["a", null],
        ItemReadOnlyList = [Item, null],
        ItemsByName = new() { ["a"] = Item, ["b"] = null },
        StringsById = new Dictionary<Guid, string?> { [Guid.Parse("8d2f6a0e-1b2c-4d5e-8f90-a1b2c3d4e5f6")] = null },
        SwappedItems = new() { ["a"] = null },
        NestedLists = [[Item, null], null],
        ListsByName = new() { ["a"] = [Item, null] },
        ListArray = [[Item, null]],
        NullableObjects = [1, null],
        Objects = ["x"],
        NullableInts = [1, null],
        NotNullStrings = ["a"],
        NotNullItems = [Item],
        ItemPage = new Page<ElementItem> { Items = [Item, null] },
        IntPage = new Page<int> { Items = [1] },
        Shapes = [new ElementCircle { Radius = 1 }, null],
        Dictionaries = [new Dictionary<string, ElementItem> { ["a"] = Item }, null],
        Tints = [StjTint.Red, null],
        NumericTints = [Tint.Crimson, null],
        TintsByName = new() { ["a"] = StjTint.Plain, ["b"] = null },
        NullableJsonObjects = [new JsonObject { ["a"] = 1 }, null],
        NullableJsonArrays = [new JsonArray(1), null],
    };

    /// <summary>The wire of a place and the schema it is checked against.</summary>
    private sealed record Wire(Func<JsonNode, JsonNode> Schema, JsonNode? Value);

    private static readonly Dictionary<string, Wire> Annotated = new()
    {
        // Property places, kinds of collection, dictionary, nested generic and element.
        ["DTO collections, dictionaries and nested generics"] =
            new(d => Component("NullableElementsModel"), StjWire.Serialize(Elements(), StjWire.Mvc())),
        ["one component as a nullable element, an element and a property"] =
            new(d => Component("ElementItemPlaces"),
                StjWire.Serialize(new ElementItemPlaces { NullableItems = [Item, null], Items = [Item], Item = Item }, StjWire.Mvc())),

        // A query collection: the bound list as System.Text.Json writes it.
        ["query List<string?>"] =
            new(d => Parameter(d, "/nullable-elements/query", "tags"), StjWire.Serialize<List<string?>>(["a", null], StjWire.Mvc())),

        // Request bodies.
        ["body List<ElementItem?>"] =
            new(d => Body(d, "/nullable-elements/body"), StjWire.Serialize<List<ElementItem?>>([Item, null], StjWire.Mvc())),
        ["body Dictionary<string, ElementItem?>"] =
            new(d => Body(d, "/nullable-elements/body-dictionary"),
                StjWire.Serialize(new Dictionary<string, ElementItem?> { ["a"] = Item, ["b"] = null }, StjWire.Mvc())),
        ["body List<List<ElementItem?>?>"] =
            new(d => Body(d, "/nullable-elements/body-nested"),
                StjWire.Serialize<List<List<ElementItem?>?>>([[Item, null], null], StjWire.Mvc())),

        // Responses from the signature.
        ["response List<ElementItem?>"] =
            new(d => Response(d, "/nullable-elements/response"), StjWire.Serialize<List<ElementItem?>>([Item, null], StjWire.Mvc())),
        ["response Dictionary<string, ElementItem?>"] =
            new(d => Response(d, "/nullable-elements/response-dictionary"),
                StjWire.Serialize(new Dictionary<string, ElementItem?> { ["a"] = Item, ["b"] = null }, StjWire.Mvc())),
        ["response List<List<ElementItem?>?>"] =
            new(d => Response(d, "/nullable-elements/response-nested"),
                StjWire.Serialize<List<List<ElementItem?>?>>([[Item, null], null], StjWire.Mvc())),
    };

    /// <summary>The same places with <c>null</c> in one position that carries no <c>?</c>.</summary>
    private static readonly Dictionary<string, Wire> NotAnnotated = new()
    {
        ["DTO List<ElementItem> element"] = new(d => Component("NullableElementsModel"), Edited(w => w["notNullItems"] = new JsonArray((JsonNode?)null))),
        ["DTO List<string> element"] = new(d => Component("NullableElementsModel"), Edited(w => w["notNullStrings"] = new JsonArray((JsonNode?)null))),
        ["DTO list value of Dictionary<string, List<ElementItem?>>"] = new(d => Component("NullableElementsModel"), Edited(w => w["listsByName"]!["a"] = null)),
        ["DTO list element of List<ElementItem?>[]"] = new(d => Component("NullableElementsModel"), Edited(w => w["listArray"] = new JsonArray((JsonNode?)null))),
        // Not here: null as a value of the nullable dictionary element of List<Dictionary<string, ElementItem>?>.
        // The evaluator does not apply additionalProperties when the type lists several types (see the
        // remarks of SchemaConformance; still so in Corvus.Json.Validator 5.7.7), so it cannot reject it;
        // the value schema of that element is checked by its form in the schema tests.
        ["DTO element of List<ElementItem> beside nullable elements"] =
            new(d => Component("ElementItemPlaces"),
                StjWire.Serialize(new ElementItemPlaces { NullableItems = [null], Items = [null!], Item = Item }, StjWire.Mvc())),
        ["query List<string?> itself"] = new(d => Parameter(d, "/nullable-elements/query", "tags"), null),
        ["body List<ElementItem?> itself"] = new(d => Body(d, "/nullable-elements/body"), null),
        ["body Dictionary<string, ElementItem?> itself"] = new(d => Body(d, "/nullable-elements/body-dictionary"), null),
        ["body List<List<ElementItem?>?> itself"] = new(d => Body(d, "/nullable-elements/body-nested"), null),
        ["response List<ElementItem?> itself"] = new(d => Response(d, "/nullable-elements/response"), null),
        ["response Dictionary<string, ElementItem?> itself"] = new(d => Response(d, "/nullable-elements/response-dictionary"), null),
        ["response List<List<ElementItem?>?> itself"] = new(d => Response(d, "/nullable-elements/response-nested"), null),
    };

    public static TheoryData<OpenApiSpecVersion, string> AnnotatedCases => Cases(Annotated);
    public static TheoryData<OpenApiSpecVersion, string> NotAnnotatedCases => Cases(NotAnnotated);

    [Theory]
    [MemberData(nameof(AnnotatedCases))]
    public void NullInAnAnnotatedPosition_PassesTheSchemaOfThePlace(OpenApiSpecVersion version, string place)
    {
        var wire = Annotated[place];

        var result = Validate(version, wire);

        result.IsValid.Should().BeTrue(because: $"{place}: {string.Join("; ", result.Errors)}");
    }

    [Theory]
    [MemberData(nameof(NotAnnotatedCases))]
    public void NullInAPositionWithoutAnnotation_IsRejectedByTheSchemaOfThePlace(OpenApiSpecVersion version, string place)
    {
        var wire = NotAnnotated[place];

        var result = Validate(version, wire);

        result.IsValid.Should().BeFalse(because: $"{place} carries no '?', so null is not a value of it: {wire.Value?.ToJsonString() ?? "null"}");
    }

    // ── The JSON Lines item of a sequence of lists of nullable items ──────────────────────────

    /// <summary>
    /// The item schema of <c>application/jsonl</c> is an annotation the document evaluator does not
    /// apply, so it is taken from the document and checked against one written item directly.
    /// </summary>
    private JsonNode ItemSchema(OpenApiSpecVersion version)
    {
        var media = fixture.Documents[version]["paths"]!["/nullable-elements/response-stream-jsonl"]!["get"]!
            ["responses"]!["200"]!["content"]!["application/jsonl"]!;
        var key = version == OpenApiSpecVersion.OpenApi3_2 ? "itemSchema" : "x-oai-itemSchema";
        return media[key] ?? throw new InvalidOperationException($"The JSON Lines media type of {version} has no '{key}'.");
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void JsonLinesItem_WithNullInsideTheList_PassesTheItemSchema(OpenApiSpecVersion version)
    {
        var item = StjWire.Serialize<List<ElementItem?>>([Item, null], StjWire.Mvc());

        var result = fixture.Conformance[version].ValidateSchema(ItemSchema(version), item);

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void JsonLinesItem_ThatIsNullItself_IsRejectedByTheItemSchema(OpenApiSpecVersion version)
    {
        var result = fixture.Conformance[version].ValidateSchema(ItemSchema(version), null);

        result.IsValid.Should().BeFalse(because: "the item of IAsyncEnumerable<List<ElementItem?>> is a list without '?'");
    }

    // ── The keywords next to $ref mean what the allOf wrapper meant ──────────────────────────

    /// <summary>Instances checked against both forms of a place.</summary>
    private static readonly (string Name, JsonNode? Value)[] Instances =
    [
        ("object of the component", new JsonObject { ["name"] = "x" }),
        ("string longer than the bound", JsonValue.Create("longer-than-five")),
        ("allowed string", JsonValue.Create("low")),
        ("null", null),
    ];

    private Dictionary<string, bool> Verdicts(OpenApiSpecVersion version, JsonNode schema, (string Name, JsonNode? Value)[] instances) =>
        instances.ToDictionary(i => i.Name, i => fixture.Conformance[version].ValidateSchema(schema, i.Value).IsValid);

    /// <summary>
    /// The places of the generated document: the new form of 3.1/3.2 is folded, the old form of the
    /// 3.0 output is the wrapper with the same siblings and no keyword that exists only in 3.0, and
    /// both are evaluated against the components of the 3.1/3.2 document on the same instances.
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, "lengths", new[] { "minLength", "maxLength" })]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, "lengths", new[] { "minLength", "maxLength" })]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, "allowed", new[] { "enum", "not" })]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, "allowed", new[] { "enum", "not" })]
    public void GeneratedPlace_InTheNewForm_GivesTheVerdictsOfThe30Wrapper(OpenApiSpecVersion version, string property, string[] siblings)
    {
        var place = RefSiblingsProperty(fixture.Documents[version], property).AsObject();
        var wrapper = RefSiblingsProperty(fixture.Documents[OpenApiSpecVersion.OpenApi3_0], property).AsObject();

        place.Should().ContainKey("$ref").And.NotContainKey("allOf", because: "3.1 and later fold the wrapper");
        var allOf = wrapper["allOf"]!.AsArray();
        allOf[0]!["$ref"]!.GetValue<string>().Should().Be(place["$ref"]!.GetValue<string>());
        // A keyword of the old form stands on the wrapper, or — the allowed values — in the {enum} element of allOf.
        foreach (var sibling in siblings)
            (wrapper[sibling] ?? allOf.Skip(1).Select(e => e![sibling]).Single(v => v is not null))!.ToJsonString()
                .Should().Be(place[sibling]!.ToJsonString(), because: $"'{sibling}' keeps its value");
        wrapper.Should().NotContainKey("nullable").And.NotContainKey("exclusiveMinimum").And.NotContainKey("exclusiveMaximum",
            because: "a keyword that exists only in 3.0 would make the 3.0 node meaningless for a 2020-12 evaluator");

        Verdicts(version, place, Instances).Should().Equal(Verdicts(version, wrapper, Instances));
    }

    /// <summary>The nullable form of a place with siblings is <c>anyOf</c> with the siblings beside it, and admits null.</summary>
    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void GeneratedNullablePlace_InTheNewForm_AdmitsNullAndTheComponent(OpenApiSpecVersion version)
    {
        var place = RefSiblingsProperty(fixture.Documents[version], "nullableDescribed").AsObject();

        place.Should().ContainKey("anyOf").And.ContainKey("maxLength").And.NotContainKey("allOf");
        Verdicts(version, place, Instances).Should().Equal(new Dictionary<string, bool>
        {
            ["object of the component"] = true,
            ["string longer than the bound"] = false,
            ["allowed string"] = false,
            ["null"] = true,
        });
    }

    /// <summary>
    /// The generator writes value constraints next to a reference only to components that are objects,
    /// where <c>maxLength</c> and a string <c>enum</c> do not constrain an instance; a synthetic place
    /// referencing a string component shows the siblings applying together with <c>$ref</c>. The old
    /// form is the wrapper the generator builds, <c>{allOf: [$ref, {enum}], maxLength}</c>; the new form
    /// is what <see cref="ReferencePlace.Finish"/> makes of it for the version.
    /// </summary>
    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public async Task ConstrainedPlaceOfAStringComponent_InTheNewForm_GivesTheVerdictsOfTheWrapper(OpenApiSpecVersion version)
    {
        static OpenApiSchema Wrapper() => new()
        {
            AllOf = [new OpenApiSchemaReference("Code", null), new OpenApiSchema { Enum = [JsonValue.Create("ab"), JsonValue.Create("abcd")] }],
            MaxLength = 3,
        };

        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "1" },
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["Holder"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["old"] = Wrapper(),
                            ["new"] = ReferencePlace.Finish(Wrapper(), version),
                        },
                    },
                },
            },
        };
        var written = await VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, TestContext.Current.CancellationToken);
        var properties = written["components"]!["schemas"]!["Holder"]!["properties"]!;
        var conformance = SchemaConformance.For(written);

        properties["new"]!.AsObject().Should().ContainKeys("$ref", "maxLength", "enum").And.NotContainKey("allOf");

        (string Name, JsonNode? Value)[] instances =
        [
            ("allowed and short", JsonValue.Create("ab")),
            ("allowed but too long", JsonValue.Create("abcd")),
            ("short but not allowed", JsonValue.Create("zz")),
            ("null", null),
        ];
        Dictionary<string, bool> VerdictsOf(string form) =>
            instances.ToDictionary(i => i.Name, i => conformance.ValidateSchema(properties[form]!, i.Value).IsValid);

        var expected = new Dictionary<string, bool>
        {
            ["allowed and short"] = true,
            ["allowed but too long"] = false,
            ["short but not allowed"] = false,
            ["null"] = false,
        };
        new { Old = VerdictsOf("old"), New = VerdictsOf("new") }.Should().BeEquivalentTo(new { Old = expected, New = expected });
    }

    // ── Places in the document ────────────────────────────────────────────────────────────────

    private ConformanceResult Validate(OpenApiSpecVersion version, Wire wire) =>
        fixture.Conformance[version].ValidateSchema(wire.Schema(fixture.Documents[version]), wire.Value);

    private static JsonNode Component(string id) => new JsonObject { ["$ref"] = $"#/components/schemas/{id}" };

    private static JsonNode Parameter(JsonNode document, string path, string name) =>
        document["paths"]![path]!["get"]!["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!["schema"]!;

    private static JsonNode Body(JsonNode document, string path) =>
        document["paths"]![path]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;

    private static JsonNode Response(JsonNode document, string path) =>
        document["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!;

    private static JsonNode RefSiblingsProperty(JsonNode document, string property) =>
        document["components"]!["schemas"]!["RefSiblingsModel"]!["properties"]![property]!;

    /// <summary>The DTO wire of <see cref="Elements"/> with one position edited.</summary>
    private static JsonNode Edited(Action<JsonNode> edit)
    {
        var wire = StjWire.Serialize(Elements(), StjWire.Mvc())!;
        edit(wire);
        return wire;
    }

    private static TheoryData<OpenApiSpecVersion, string> Cases(Dictionary<string, Wire> table)
    {
        var data = new TheoryData<OpenApiSpecVersion, string>();
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
        foreach (var place in table.Keys)
            data.Add(version, place);
        return data;
    }
}
