using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// ModernApi and LegacyNoNrtApi built for every version, serialized as JSON and as YAML (read back with
/// JSON values), with the diagnostics of each ModernApi build.
/// </summary>
public sealed class NullableElementSchemaFixture
{
    public NullableElementSchemaFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (modern, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic =>
                new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Diagnostics[version] = diagnostics;
            var legacy = VersionedDocumentHarness.Build(VersionedDocumentHarness.LegacyNoNrtApiOptions(version));
            foreach (var format in new[] { DocumentFormat.Json, DocumentFormat.Yaml })
            {
                Modern[(version, format)] = Serialize(modern, version, format);
                Legacy[(version, format)] = Serialize(legacy, version, format);
            }
        }
    }

    private static JsonNode Serialize(OpenApiDocument document, OpenApiSpecVersion version, DocumentFormat format) =>
        AsJson(VersionedDocumentHarness.SerializeAsync(document, version, format, CancellationToken.None).GetAwaiter().GetResult());

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

    public Dictionary<(OpenApiSpecVersion Version, DocumentFormat Format), JsonNode> Modern { get; } = [];

    public Dictionary<(OpenApiSpecVersion Version, DocumentFormat Format), JsonNode> Legacy { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];

    /// <summary>
    /// Precondition of the case: no member, type or the assembly of LegacyNoNrtApi carries a
    /// <c>NullableAttribute</c> or <c>NullableContextAttribute</c>, so no position has NRT data at all.
    /// </summary>
    public static void LegacyAssemblyHasNoNullableMetadata()
    {
        using var stream = File.OpenRead(TestPaths.LegacyNoNrtApiDll);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var attributeTypes = reader.CustomAttributes
            .Select(h => AttributeTypeName(reader, reader.GetCustomAttribute(h)))
            .ToHashSet(StringComparer.Ordinal);

        attributeTypes.Should().NotContain(
            ["System.Runtime.CompilerServices.NullableAttribute", "System.Runtime.CompilerServices.NullableContextAttribute"],
            "LegacyNoNrtApi is compiled without nullable reference types");
    }

    private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        EntityHandle type = attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            _ => default,
        };
        return type.Kind switch
        {
            HandleKind.TypeDefinition => Name(reader, reader.GetTypeDefinition((TypeDefinitionHandle)type).Namespace, reader.GetTypeDefinition((TypeDefinitionHandle)type).Name),
            HandleKind.TypeReference => Name(reader, reader.GetTypeReference((TypeReferenceHandle)type).Namespace, reader.GetTypeReference((TypeReferenceHandle)type).Name),
            _ => string.Empty,
        };
    }

    private static string Name(MetadataReader reader, StringHandle ns, StringHandle name) =>
        $"{reader.GetString(ns)}.{reader.GetString(name)}";
}

/// <summary>
/// Elements of collections and arrays and dictionary values of DTO properties: the position annotated
/// <c>?</c> (NRT byte 2) of a reference type has the nullable form of the version, every other position
/// keeps its schema.
/// </summary>
/// <remarks>
/// <para>
/// Places: <c>ModernApi.Models.NullableElements</c> and <c>LegacyNoNrtApi.Models.LegacyCollectionsModel</c>;
/// the expected positions are the <c>?</c> of their declarations. The nullable forms are those of the field
/// catalog (<c>schema.nullable</c>, <c>schema.anyOf</c>): a schema with a type gets <c>nullable: true</c>
/// in 3.0 and <c>"null"</c> among its types in 3.1+; a reference becomes
/// <c>anyOf: [$ref, null branch]</c>, the null branch being <c>{type: object, nullable: true, enum: [null]}</c>
/// in 3.0 and <c>{type: "null"}</c> in 3.1+.
/// </para>
/// </remarks>
public class NullableElementSchemaTests(NullableElementSchemaFixture fixture) : IClassFixture<NullableElementSchemaFixture>
{
    private const string Model = "NullableElementsModel";
    private const string Item = "#/components/schemas/ElementItem";

    private static readonly DocumentFormat[] Formats = [DocumentFormat.Json, DocumentFormat.Yaml];

    /// <summary>The expected schema of a position in 3.0 and in 3.1+.</summary>
    private sealed record Form(string V30, string V31)
    {
        public string For(OpenApiSpecVersion version) => version == OpenApiSpecVersion.OpenApi3_0 ? V30 : V31;
    }

    private static Form Same(string schema) => new(schema, schema);

    private static Form NullableType(string type, string? format = null)
    {
        var formatKey = format is null ? "" : $$""", "format": "{{format}}" """;
        return new($$"""{"type": "{{type}}", "nullable": true{{formatKey}}}""", $$"""{"type": ["{{type}}", "null"]{{formatKey}}}""");
    }

    private static Form NullableRef(string reference) => new(
        $$"""{"anyOf": [{"$ref": "{{reference}}"}, {"type": "object", "nullable": true, "enum": [null]}]}""",
        $$"""{"anyOf": [{"$ref": "{{reference}}"}, {"type": "null"}]}""");

    private static readonly string ItemRef = $$"""{"$ref": "{{Item}}"}""";

    private static readonly Form NullableItem = NullableRef(Item);

    /// <summary>One position of a DTO property: the JSON pointer below the property schema and its expected schema.</summary>
    private sealed record Position(string Component, string Property, string Pointer, Form Expected);

    private static Position P(string property, string pointer, Form expected) => new(Model, property, pointer, expected);

    private static readonly Position[] ModernPositions =
    [
        // Collections: the element is nullable, the collection is not (checked below).
        P("strings", "/items", NullableType("string")),
        P("itemArray", "/items", NullableItem),
        P("itemSequence", "/items", NullableItem),
        P("stringSet", "/items", NullableType("string")),
        P("itemReadOnlyList", "/items", NullableItem),

        // Dictionaries: the value is nullable, the key is not.
        P("itemsByName", "/additionalProperties", NullableItem),
        P("stringsById", "/additionalProperties", NullableType("string")),
        P("swappedItems", "/additionalProperties", NullableItem),

        // Nested generics: exactly the positions with ?.
        P("nestedLists", "/items/type", new(""" "array" """, """["array", "null"]""")),
        P("nestedLists", "/items/nullable", new("true", "null")),
        P("nestedLists", "/items/items", NullableItem),
        P("listsByName", "/additionalProperties", Same("""{"type": "array", "items": ITEM}""".Replace("ITEM", NullableItemPlaceholder))),
        P("listArray", "/items", Same("""{"type": "array", "items": ITEM}""".Replace("ITEM", NullableItemPlaceholder))),

        // Schemas that already admit null or are a value type, and positions without ?.
        P("nullableObjects", "/items", Same("{}")),
        P("objects", "/items", Same("{}")),
        P("nullableInts", "/items", NullableType("integer", "int32")),
        P("notNullStrings", "/items", Same("""{"type": "string"}""")),
        P("notNullItems", "/items", Same(ItemRef)),

        // Other kinds of element schemas: a polymorphic base and a dictionary.
        P("shapes", "/items", NullableRef("#/components/schemas/ElementShape")),
        P("dictionaries", "/items", new(
            $$"""{"type": "object", "nullable": true, "additionalProperties": {{ItemRef}}}""",
            $$"""{"type": ["object", "null"], "additionalProperties": {{ItemRef}}}""")),

        // One component in three places.
        new("ElementItemPlaces", "nullableItems", "/items", NullableItem),
        new("ElementItemPlaces", "items", "/items", Same(ItemRef)),

        // Positions declared in a #nullable disable block (byte 0).
        new("ObliviousElementsModel", "strings", "/items", Same("""{"type": "string"}""")),
        new("ObliviousElementsModel", "items", "/items", Same(ItemRef)),
        new("ObliviousElementsModel", "itemsByName", "/additionalProperties", Same(ItemRef)),
        new("ObliviousElementsModel", "itemArray", "/items", Same(ItemRef)),
    ];

    /// <summary>Stands for the nullable item form inside a composed literal; replaced per version.</summary>
    private const string NullableItemPlaceholder = "\"NULLABLE_ITEM\"";

    public static TheoryData<OpenApiSpecVersion, DocumentFormat, int> ModernCases() => Cases(ModernPositions.Length);

    private static TheoryData<OpenApiSpecVersion, DocumentFormat, int> Cases(int count)
    {
        var data = new TheoryData<OpenApiSpecVersion, DocumentFormat, int>();
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
        for (var i = 0; i < count; i++)
            data.Add(version, format, i);
        return data;
    }

    public static TheoryData<OpenApiSpecVersion, DocumentFormat> AllForms()
    {
        var data = new TheoryData<OpenApiSpecVersion, DocumentFormat>();
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
            data.Add(version, format);
        return data;
    }

    private static JsonNode? Expected(Form form, OpenApiSpecVersion version) =>
        JsonNode.Parse(form.For(version).Replace(NullableItemPlaceholder, NullableItem.For(version)));

    private static JsonNode Component(JsonNode document, string id) =>
        document["components"]!["schemas"]![id] ?? throw new InvalidOperationException($"No component {id}.");

    private static JsonNode PropertySchema(JsonNode document, string component, string property) =>
        Component(document, component)["properties"]![property]
            ?? throw new InvalidOperationException($"No property {component}.{property}.");

    /// <summary>The node at a JSON pointer below <paramref name="node"/>; <see langword="null"/> when a key is missing.</summary>
    private static JsonNode? At(JsonNode node, string pointer)
    {
        JsonNode? current = node;
        foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
            current = current is JsonObject o && o.TryGetPropertyValue(segment, out var next) ? next : null;
        return current;
    }

    /// <summary>The JSON with every <c>type</c> list sorted, the order of types carrying no meaning.</summary>
    private static JsonNode? Normalized(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (key, value) in o)
                    copy[key] = key == "type" && value is JsonArray types
                        ? new JsonArray(types.Select(t => (JsonNode?)JsonValue.Create(t!.GetValue<string>())).OrderBy(t => t!.GetValue<string>(), StringComparer.Ordinal).ToArray())
                        : Normalized(value);
                return copy;
            case JsonArray a:
                return new JsonArray(a.Select(Normalized).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    /// <summary><see cref="Normalized"/>, and a <c>type</c> list given on its own sorted as well.</summary>
    private static JsonNode? NormalizedRoot(JsonNode? node) =>
        node is JsonArray list && list.All(t => t is JsonValue v && v.TryGetValue<string>(out _))
            ? new JsonArray(list.Select(t => t!.GetValue<string>()).Order(StringComparer.Ordinal).Select(t => (JsonNode?)JsonValue.Create(t)).ToArray())
            : Normalized(node);

    private static void ShouldBe(JsonNode? actual, JsonNode? expected, string because) =>
        JsonNode.DeepEquals(NormalizedRoot(actual), NormalizedRoot(expected)).Should().BeTrue(
            $"{because} must be {expected?.ToJsonString() ?? "absent"}, got {actual?.ToJsonString() ?? "absent"}");

    // --- Positions of DTO properties -----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ModernCases))]
    public void ElementAndValuePositions_HaveTheNullableFormOfTheVersion_ExactlyWhereAnnotated(
        OpenApiSpecVersion version, DocumentFormat format, int index)
    {
        var position = ModernPositions[index];
        var property = PropertySchema(fixture.Modern[(version, format)], position.Component, position.Property);

        ShouldBe(At(property, position.Pointer), Expected(position.Expected, version),
            $"{position.Component}.{position.Property}{position.Pointer}");
    }

    public static TheoryData<string, string> AnnotatedContainers() => new()
    {
        { "strings", "array" },
        { "itemArray", "array" },
        { "itemSequence", "array" },
        { "stringSet", "array" },
        { "itemReadOnlyList", "array" },
        { "itemsByName", "object" },
        { "stringsById", "object" },
        { "swappedItems", "object" },
        { "nestedLists", "array" },
        { "listsByName", "object" },
        { "listArray", "array" },
        { "shapes", "array" },
        { "dictionaries", "array" },
        { "nullableJsonObjects", "array" },
        { "nullableJsonArrays", "array" },
    };

    [Theory]
    [MemberData(nameof(AnnotatedContainers))]
    public void TheCollectionOrDictionaryItself_KeepsItsSchema(string property, string type)
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
        {
            var schema = PropertySchema(fixture.Modern[(version, format)], Model, property);
            schema["type"]!.ToJsonString().Should().Be($"\"{type}\"", $"{property} {version} {format}");
            schema.AsObject().ContainsKey("nullable").Should().BeFalse($"{property} {version} {format}");
            schema.AsObject().ContainsKey("anyOf").Should().BeFalse($"{property} {version} {format}");
        }
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public void DictionaryKeys_KeepTheirSchema(OpenApiSpecVersion version, DocumentFormat format)
    {
        var document = fixture.Modern[(version, format)];

        // A Guid key is a uuid string in 3.1+; a string key has no propertyNames; 3.0 has no propertyNames.
        var guidKeys = At(PropertySchema(document, Model, "stringsById"), "/propertyNames");
        ShouldBe(guidKeys, version == OpenApiSpecVersion.OpenApi3_0 ? null : JsonNode.Parse("""{"type": "string", "format": "uuid"}"""),
            "stringsById propertyNames");
        foreach (var property in new[] { "itemsByName", "swappedItems", "listsByName" })
            At(PropertySchema(document, Model, property), "/propertyNames").Should().BeNull(property);
    }

    // --- The enum element is the union of the enum and the null branch ----------------------------------

    [Theory]
    [MemberData(nameof(AllForms))]
    public void NullableEnumElement_IsTheUnionOfTheEnumAndTheNullBranch(OpenApiSpecVersion version, DocumentFormat format)
    {
        var items = At(PropertySchema(fixture.Modern[(version, format)], Model, "tints"), "/items")!;

        // Nullable<StjTint>: anyOf of the string enum, which keeps one type, and the version's null branch.
        items.AsObject().ContainsKey("type").Should().BeFalse();
        var anyOf = items["anyOf"]!.AsArray();
        anyOf.Should().HaveCount(2);
        anyOf[0]!["type"]!.GetValue<string>().Should().Be("string");
        anyOf[0]!.AsObject().ContainsKey("nullable").Should().BeFalse();
        if (version == OpenApiSpecVersion.OpenApi3_0)
        {
            anyOf[1]!["nullable"]!.GetValue<bool>().Should().BeTrue();
            ShouldBe(anyOf[1]!["enum"], JsonNode.Parse("[null]"), "tints null branch");
        }
        else
        {
            anyOf[1]!["type"]!.GetValue<string>().Should().Be("null");
        }
    }

    // --- Page<T> with List<T?> -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllForms))]
    public void PageOfT_ClosedByAReference_HasNullableItems_AndClosedByAValueType_DoesNot(
        OpenApiSpecVersion version, DocumentFormat format)
    {
        var document = fixture.Modern[(version, format)];

        var itemPage = Component(document, ReferencedId(PropertySchema(document, Model, "itemPage")));
        ShouldBe(At(itemPage, "/properties/items/items"), Expected(NullableItem, version), "Page<ElementItem>.items");

        var intPage = Component(document, ReferencedId(PropertySchema(document, Model, "intPage")));
        ShouldBe(At(intPage, "/properties/items/items"), JsonNode.Parse("""{"type": "integer", "format": "int32"}"""), "Page<int>.items");
    }

    /// <summary>The component id a property references, directly or through the 3.0 wrapper.</summary>
    private static string ReferencedId(JsonNode property)
    {
        var reference = property["$ref"] ?? property["allOf"]?[0]?["$ref"]
            ?? throw new InvalidOperationException($"No reference in {property.ToJsonString()}.");
        return reference.GetValue<string>().Split('/')[^1];
    }

    // --- The polymorphic union component is not changed ----------------------------------------------

    [Theory]
    [MemberData(nameof(AllForms))]
    public void PolymorphicBaseElement_LeavesTheUnionComponentWithoutANullBranch(OpenApiSpecVersion version, DocumentFormat format)
    {
        var union = Component(fixture.Modern[(version, format)], "ElementShape");

        // Neither the 3.0 null branch (nullable, enum: [null]) nor the 3.1+ one (type "null") is in it.
        union.ToJsonString().Should().NotContain("\"null\"").And.NotContain("\"nullable\":");
    }

    // --- One component in three places: the component is unchanged -----------------------------------

    [Theory]
    [MemberData(nameof(AllForms))]
    public void OneComponentInThreePlaces_EachPlaceHasItsForm_AndTheComponentIsUnchanged(
        OpenApiSpecVersion version, DocumentFormat format)
    {
        var document = fixture.Modern[(version, format)];

        // The described property: the description beside $ref in 3.1+, on the allOf wrapper in 3.0.
        var item = PropertySchema(document, "ElementItemPlaces", "item");
        var description = JsonValue.Create("The item of this place, described here.");
        ShouldBe(item, version == OpenApiSpecVersion.OpenApi3_0
            ? new JsonObject { ["allOf"] = new JsonArray(JsonNode.Parse(ItemRef)), ["description"] = description }
            : new JsonObject { ["$ref"] = Item, ["description"] = description },
            "ElementItemPlaces.item");

        // The component: its own description and property, no null form and no description of a place.
        var component = Component(document, "ElementItem");
        component["type"]!.ToJsonString().Should().Be("\"object\"");
        component["description"]!.GetValue<string>().Should().Be("An element component, referenced from nullable and non-nullable positions.");
        ShouldBe(component["properties"], JsonNode.Parse("""{"name": {"type": "string", "description": "Name."}}"""), "ElementItem.properties");
        component.ToJsonString().Should().NotContain("\"null\"").And.NotContain("\"nullable\":").And.NotContain("The item of this place");
    }

    // --- The BCL JSON object and array, nullable in one place, plain in another ----------------------

    [Theory]
    [MemberData(nameof(AllForms))]
    public void BclJsonObjectAndArray_AreNullableOnlyWhereAnnotated(OpenApiSpecVersion version, DocumentFormat format)
    {
        var document = fixture.Modern[(version, format)];

        foreach (var (nullable, plain, type) in new[]
                 {
                     ("nullableJsonObjects", "jsonObjects", "object"),
                     ("nullableJsonArrays", "jsonArrays", "array"),
                 })
        {
            var nullableItems = At(PropertySchema(document, Model, nullable), "/items")!;
            var plainItems = At(PropertySchema(document, "JsonElementsModel", plain), "/items")!;

            // One assertion over both places: the nullable form where annotated, none in the other place.
            var observed = new JsonObject
            {
                ["nullable"] = new JsonObject { ["type"] = nullableItems["type"]?.DeepClone(), ["nullable"] = nullableItems["nullable"]?.DeepClone() },
                ["plain"] = new JsonObject { ["type"] = plainItems["type"]?.DeepClone(), ["nullable"] = plainItems["nullable"]?.DeepClone() },
            };
            var expected = version == OpenApiSpecVersion.OpenApi3_0
                ? new JsonObject
                {
                    ["nullable"] = new JsonObject { ["type"] = type, ["nullable"] = true },
                    ["plain"] = new JsonObject { ["type"] = type, ["nullable"] = null },
                }
                : new JsonObject
                {
                    ["nullable"] = new JsonObject { ["type"] = new JsonArray(type, "null"), ["nullable"] = null },
                    ["plain"] = new JsonObject { ["type"] = type, ["nullable"] = null },
                };
            ShouldBe(observed, expected, $"{nullable} / {plain}");
        }
    }

    // --- The assembly without nullable reference types -----------------------------------------------

    public static TheoryData<string, string> LegacyPositions() => new()
    {
        { "strings", "/items" },
        { "itemsByName", "/additionalProperties" },
        { "nestedLists", "/items/items" },
        { "nestedLists", "/items" },
        { "itemArray", "/items" },
    };

    [Theory]
    [MemberData(nameof(LegacyPositions))]
    public void AssemblyWithoutNrt_HasNoNullableElementsOrValues(string property, string pointer)
    {
        NullableElementSchemaFixture.LegacyAssemblyHasNoNullableMetadata();

        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var format in Formats)
        {
            var position = At(PropertySchema(fixture.Legacy[(version, format)], "LegacyCollectionsModel", property), pointer)!;
            var because = $"LegacyCollectionsModel.{property}{pointer} {version} {format}";

            position.AsObject().ContainsKey("nullable").Should().BeFalse(because);
            position.AsObject().ContainsKey("anyOf").Should().BeFalse(because);
            position["type"]?.ToJsonString().Should().NotContain("null", because);
        }
    }

    // --- No diagnostics for the places ---------------------------------------------------------------

    [Fact]
    public void ThePlaces_GetNoDiagnostics()
    {
        string[] components = [Model, "JsonElementsModel", "ElementItemPlaces", "ObliviousElementsModel", "ElementItem", "ElementShape"];
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            fixture.Diagnostics[version]
                .Where(d => d.Location is { } location
                    && components.Any(c => location.StartsWith($"#/components/schemas/{c}", StringComparison.Ordinal)))
                .Should().BeEmpty($"{version}");
        }
    }
}
