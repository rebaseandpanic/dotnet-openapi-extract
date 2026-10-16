using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using ModernApi.Models.NullableElements;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// A nullable enum (<c>T?</c> of an enum <c>T</c>) — a property, a parameter, a body, a collection
/// element, a dictionary value — is <c>anyOf: [enum schema, null branch of the version]</c> in every
/// version, the form of a nullable reference. The enum schema keeps its own <c>type</c>, its
/// <c>enum</c> without <c>null</c> and its <c>x-enum-varnames</c> in step with <c>enum</c>. JSON Schema
/// 2020-12 checks <c>type</c> and <c>enum</c> of one schema together, so a <c>null</c> type next to an
/// <c>enum</c> without <c>null</c> rejects the <c>null</c> System.Text.Json writes; OpenAPI 3.0.3 says the
/// same of <c>nullable: true</c> with <c>enum</c>. The union admits <c>null</c> and the listed values only.
/// </summary>
public class NullableEnumSchemaTests(NullableElementConformanceFixture fixture) : IClassFixture<NullableElementConformanceFixture>
{
    private static readonly string[] TintNames = ["ruby", "Green", "scarlet", "Plain"];
    private static readonly string[] TintVarnames = ["Red", "Green", "Crimson", "Plain"];

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];
    public static TheoryData<OpenApiSpecVersion> SchemaVersions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    // ── Places ───────────────────────────────────────────────────────────────────────

    private static JsonNode Schemas(JsonNode document) => document["components"]!["schemas"]!;

    /// <summary>Every place of a nullable enum in the document: its schema, and the enum it lists.</summary>
    private static IEnumerable<(string Place, JsonNode Schema, JsonSchemaType Type, string[] Values)> Places(JsonNode document)
    {
        var model = Schemas(document)["NullableElementsModel"]!["properties"]!;
        var wire = Schemas(document)["EnumWireNamesModel"]!["properties"]!;
        var described = Schemas(document)["DescriptionPriorityModel"]!["properties"]!;
        var paths = document["paths"]!;

        yield return ("property, converter on the type", wire["nullableTypeConverter"]!, JsonSchemaType.String, TintNames);
        yield return ("property, converter on the property", wire["nullablePropertyStj"]!, JsonSchemaType.String, TintNames);
        yield return ("property with an attribute description", described["kindWithAttribute"]!, JsonSchemaType.String, ["One", "Two"]);
        yield return ("element written as strings", model["tints"]!["items"]!, JsonSchemaType.String, TintNames);
        yield return ("element written as numbers", model["numericTints"]!["items"]!, JsonSchemaType.Integer, ["0", "1", "2", "3"]);
        yield return ("dictionary value", model["tintsByName"]!["additionalProperties"]!, JsonSchemaType.String, TintNames);
        yield return ("query parameter",
            paths["/nullable-elements/query-enum"]!["get"]!["parameters"]!.AsArray().Single(p => (string?)p!["name"] == "tint")!["schema"]!,
            JsonSchemaType.String, ["Red", "Green", "Crimson", "Plain"]);
        yield return ("request body",
            paths["/nullable-elements/body-enum"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!,
            JsonSchemaType.String, TintNames);
    }

    private static string TypeName(JsonSchemaType type) => type == JsonSchemaType.Integer ? "integer" : "string";

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NullableEnum_IsAnyOfTheEnumAndTheNullBranch_InEveryPlace(OpenApiSpecVersion version)
    {
        foreach (var (place, schema, type, values) in Places(fixture.Documents[version]))
        {
            var union = schema.AsObject();
            union.Should().NotContainKey("type", because: $"{place}: the union has no type of its own");
            union.Should().NotContainKey("nullable", because: place);
            var anyOf = union["anyOf"]!.AsArray();
            anyOf.Should().HaveCount(2, because: place);

            var enumSchema = anyOf[0]!.AsObject();
            enumSchema["type"]!.GetValue<string>().Should().Be(TypeName(type), because: $"{place}: one type, without null");
            enumSchema.Should().NotContainKey("nullable", because: place);
            enumSchema["enum"]!.AsArray().Select(v => v!.ToJsonString().Trim('"')).Should().Equal(values, because: $"{place}: no null among the values");
            if (enumSchema["x-enum-varnames"] is JsonArray varnames)
                varnames.Should().HaveCount(enumSchema["enum"]!.AsArray().Count, because: $"{place}: names in step with the values");

            var nullBranch = anyOf[1]!.AsObject();
            if (version == OpenApiSpecVersion.OpenApi3_0)
            {
                nullBranch["type"]!.GetValue<string>().Should().Be("object", because: place);
                nullBranch["nullable"]!.GetValue<bool>().Should().BeTrue(because: place);
                nullBranch["enum"]!.ToJsonString().Should().Be("[null]", because: place);
            }
            else
            {
                nullBranch["type"]!.GetValue<string>().Should().Be("null", because: place);
                nullBranch.Count.Should().Be(1, because: place);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void TheEnumSchemaOfANullablePlace_KeepsItsNamesAndDescriptions(OpenApiSpecVersion version)
    {
        var place = Schemas(fixture.Documents[version])["EnumWireNamesModel"]!["properties"]!["nullableTypeConverter"]!;
        var enumSchema = place["anyOf"]![0]!;

        enumSchema["x-enum-varnames"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal(TintVarnames);
        enumSchema["x-enum-descriptions"]!.AsArray().Should().HaveCount(TintNames.Length);
        enumSchema["description"]!.GetValue<string>().Should().Contain("`ruby`").And.Contain("`Plain`");
    }

    // ── What System.Text.Json writes ──────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void NullAndTheValuesStjWrites_PassTheSchemaOfThePlace_AndAnotherValueFails(OpenApiSpecVersion version)
    {
        var conformance = fixture.Conformance[version];
        var document = fixture.Documents[version];
        var model = Schemas(document)["NullableElementsModel"]!["properties"]!;
        var wire = Schemas(document)["EnumWireNamesModel"]!["properties"]!;
        var body = document["paths"]!["/nullable-elements/body-enum"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;

        var written = StjWire.Serialize(new NullableElementsModel
        {
            Tints = [StjTint.Red, null],
            NumericTints = [Tint.Crimson, null],
            TintsByName = new() { ["a"] = StjTint.Plain, ["b"] = null },
        }, StjWire.Mvc())!;
        // The converter of nullableTypeConverter is on its type: the property is written as its value.
        var typeConverter = StjWire.Serialize<StjTint?>(StjTint.Crimson, StjWire.Mvc());
        var nullProperty = StjWire.Serialize<StjTint?>(null, StjWire.Mvc());

        var accepted = new (string Place, JsonNode Schema, JsonNode? Value)[]
        {
            ("element written as strings", model["tints"]!, written["tints"]),
            ("element written as numbers", model["numericTints"]!, written["numericTints"]),
            ("dictionary value", model["tintsByName"]!, written["tintsByName"]),
            ("property, value", wire["nullableTypeConverter"]!, typeConverter),
            ("property, null", wire["nullableTypeConverter"]!, nullProperty),
            ("request body, value", body, StjWire.Serialize<StjTint?>(StjTint.Green, StjWire.Mvc())),
            ("request body, null", body, StjWire.Serialize<StjTint?>(null, StjWire.Mvc())),
        };
        written["tints"]!.AsArray()[1].Should().BeNull(because: "STJ writes the null element as null");
        nullProperty.Should().BeNull(because: "STJ writes the null property as null");
        foreach (var (place, schema, value) in accepted)
        {
            var result = conformance.ValidateSchema(schema, value);
            result.IsValid.Should().BeTrue(because: $"{place} {value?.ToJsonString() ?? "null"}: {string.Join("; ", result.Errors)}");
        }

        var rejected = new (string Place, JsonNode Schema, JsonNode? Value)[]
        {
            ("element written as strings", model["tints"]!, JsonNode.Parse("[\"violet\"]")),
            ("element written as numbers", model["numericTints"]!, JsonNode.Parse("[7]")),
            ("dictionary value", model["tintsByName"]!, JsonNode.Parse("{\"a\":\"violet\"}")),
            ("property", wire["nullableTypeConverter"]!, JsonValue.Create("violet")),
            ("request body", body, JsonValue.Create("Red")),
        };
        foreach (var (place, schema, value) in rejected)
            conformance.ValidateSchema(schema, value).IsValid.Should().BeFalse(because: $"{place}: {value!.ToJsonString()} is not a listed value");
    }

    // ── With [AllowedValues] ──────────────────────────────────────────────────────────

    /// <summary>
    /// <c>[AllowedValues]</c> without <c>null</c> narrows the union, <c>null</c> included: 3.0 keeps the
    /// outer <c>allOf</c> of the union and the allowed values; 3.1 and 3.2 put the allowed values and the
    /// keywords of the place beside <c>anyOf</c>, as for a nullable reference.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NullableEnumWithAllowedValues_KeepsTheUnion_AndTheAllowedValuesBesideIt(OpenApiSpecVersion version)
    {
        var place = Schemas(fixture.Documents[version])["EnumWireNamesModel"]!["properties"]!["nullableConstrained"]!.AsObject();

        var union = version == OpenApiSpecVersion.OpenApi3_0 ? place["allOf"]![0]!.AsObject() : place;
        union["anyOf"]!.AsArray().Should().HaveCount(2);
        union["anyOf"]![0]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal(TintNames);
        place["default"]!.GetValue<string>().Should().Be("scarlet");

        var allowed = version == OpenApiSpecVersion.OpenApi3_0 ? place["allOf"]![1]!["enum"]! : place["enum"]!;
        allowed.AsArray().Select(v => v!.GetValue<string>()).Should().Equal("ruby", "Plain");
        if (version == OpenApiSpecVersion.OpenApi3_0)
            place["allOf"]!.AsArray().Should().HaveCount(2);
        else
            place.Should().NotContainKey("allOf", because: "3.1 and later fold the outer allOf");
    }

    /// <summary>
    /// With <c>[AllowedValues]</c> the place is the outer schema — the <c>allOf</c> in 3.0, the folded
    /// <c>anyOf</c> in 3.1 and later — and it is described by the property's own documentation (its XML
    /// summary), as a nullable reference with allowed values is; the enum type's description stays on the
    /// enum schema.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NullableEnumWithAllowedValues_IsDescribedByThePropertyInEveryVersion(OpenApiSpecVersion version)
    {
        var place = Schemas(fixture.Documents[version])["EnumWireNamesModel"]!["properties"]!["nullableConstrained"]!.AsObject();
        var union = version == OpenApiSpecVersion.OpenApi3_0 ? place["allOf"]![0]!.AsObject() : place;

        place["description"]!.GetValue<string>().Should().Be("Nullable, converter on the property, with a default and allowed values.");
        union["anyOf"]![0]!["description"]!.GetValue<string>().Should().StartWith("A tint without a converter of its own.");
        if (version == OpenApiSpecVersion.OpenApi3_0)
            union.Should().NotContainKey("description", because: "the place is the outer allOf");
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void NullableEnumWithAllowedValues_AdmitsOnlyTheAllowedValues(OpenApiSpecVersion version)
    {
        var place = Schemas(fixture.Documents[version])["EnumWireNamesModel"]!["properties"]!["nullableConstrained"]!;
        var conformance = fixture.Conformance[version];

        conformance.ValidateSchema(place, JsonValue.Create("ruby")).IsValid.Should().BeTrue();
        conformance.ValidateSchema(place, JsonValue.Create("scarlet")).IsValid.Should().BeFalse(because: "listed by the enum, not allowed");
        conformance.ValidateSchema(place, null).IsValid.Should().BeFalse(because: "[AllowedValues] does not list null");
    }

    // ── With [Range] ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The places of <c>[Range(1, 2)]</c> on <c>Level?</c>, a numeric enum with the values 0 and 2: a DTO
    /// property, a request body and a query parameter.
    /// </summary>
    private static IEnumerable<(string Place, JsonNode Schema)> RangePlaces(JsonNode document)
    {
        var paths = document["paths"]!;
        yield return ("property", Schemas(document)["NullableEnumRangeModel"]!["properties"]!["level"]!);
        yield return ("request body",
            paths["/keywords/range/nullable-enum"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!);
        yield return ("query parameter",
            paths["/keywords/range/nullable-enum-query"]!["get"]!["parameters"]!.AsArray().Single(p => (string?)p!["name"] == "level")!["schema"]!);
    }

    /// <summary>
    /// <c>[Range]</c> bounds the numbers of the place (<c>minimum</c> / <c>maximum</c>), here the values of
    /// the enum: the bounds stand on the enum branch of the union, beside its values, and the null
    /// branch stays as it is.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllVersions))]
    public void RangeOnANullableNumericEnum_BoundsTheEnumBranch(OpenApiSpecVersion version)
    {
        foreach (var (place, schema) in RangePlaces(fixture.Documents[version]))
        {
            var anyOf = schema["anyOf"]!.AsArray();
            var enumBranch = anyOf[0]!.AsObject();
            enumBranch["type"]!.GetValue<string>().Should().Be("integer", because: place);
            enumBranch["enum"]!.AsArray().Select(v => v!.GetValue<int>()).Should().Equal([0, 2], because: place);
            enumBranch["minimum"]!.GetValue<decimal>().Should().Be(1, because: place);
            enumBranch["maximum"]!.GetValue<decimal>().Should().Be(2, because: place);
            enumBranch["x-enum-varnames"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal(["Low", "High"], because: place);
            anyOf[1]!.AsObject().Should().NotContainKey("minimum").And.NotContainKey("maximum", because: place);
        }
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void RangeOnANullableNumericEnum_AdmitsNullAndTheValuesInRangeOnly(OpenApiSpecVersion version)
    {
        var conformance = fixture.Conformance[version];
        var written = new
        {
            High = StjWire.Serialize<Level?>(Level.High, StjWire.Mvc()),
            Low = StjWire.Serialize<Level?>(Level.Low, StjWire.Mvc()),
            None = StjWire.Serialize<Level?>(null, StjWire.Mvc()),
        };

        foreach (var (place, schema) in RangePlaces(fixture.Documents[version]).Where(p => p.Place != "query parameter"))
        {
            conformance.ValidateSchema(schema, written.High).IsValid.Should().BeTrue(because: $"{place}: High (2) is in [1, 2]");
            conformance.ValidateSchema(schema, written.None).IsValid.Should().BeTrue(because: $"{place}: null is a value of Level?");
            conformance.ValidateSchema(schema, written.Low).IsValid.Should().BeFalse(because: $"{place}: Low (0) is below the range");
            conformance.ValidateSchema(schema, JsonValue.Create(1)).IsValid.Should().BeFalse(because: $"{place}: 1 is not a Level");
        }
    }

    /// <summary>
    /// A declared format on <c>Level?</c> describes the enum's values: it stands on the enum branch of
    /// the union, beside the type's own keywords, and neither on the union nor on the null branch.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllVersions))]
    public void FormatOnANullableEnum_StandsOnTheEnumBranch(OpenApiSpecVersion version)
    {
        var schema = Schemas(fixture.Documents[version])["NullableEnumRangeModel"]!["properties"]!["codedLevel"]!.AsObject();
        var anyOf = schema["anyOf"]!.AsArray();

        anyOf[0]!["format"]!.GetValue<string>().Should().Be("level-code");
        anyOf[0]!["enum"]!.AsArray().Select(v => v!.GetValue<int>()).Should().Equal([0, 2]);
        anyOf[1]!.AsObject().Should().NotContainKey("format");
        schema.Should().NotContainKey("format");
    }

    /// <summary>
    /// The lengths, the pattern and the format of <c>Level?</c> written by name stand on the enum branch,
    /// beside its string values — also when <c>[AllowedValues]</c> wraps the union in an outer
    /// <c>allOf</c> (3.0) — and neither on the union nor on the null branch.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllVersions))]
    public void LengthPatternAndFormatOnANullableStringEnum_StandOnTheEnumBranch(OpenApiSpecVersion version)
    {
        var properties = Schemas(fixture.Documents[version])["NullableEnumRangeModel"]!["properties"]!;

        var named = properties["namedLevel"]!.AsObject();
        var branch = named["anyOf"]![0]!.AsObject();
        branch["enum"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal(["Low", "High"]);
        branch["maxLength"]!.GetValue<int>().Should().Be(8);
        branch["minLength"]!.GetValue<int>().Should().Be(2);
        branch["pattern"]!.GetValue<string>().Should().Be("^[A-Z][a-z]+$");
        branch["format"]!.GetValue<string>().Should().Be("level-name");
        foreach (var key in new[] { "maxLength", "minLength", "pattern", "format" })
        {
            named.Should().NotContainKey(key);
            named["anyOf"]![1]!.AsObject().Should().NotContainKey(key);
        }

        var allowed = properties["allowedNamedLevel"]!.AsObject();
        var union = version == OpenApiSpecVersion.OpenApi3_0 ? allowed["allOf"]![0]!.AsObject() : allowed;
        union["anyOf"]![0]!["maxLength"]!.GetValue<int>().Should().Be(4);
        allowed.Should().NotContainKey("maxLength");
    }

    /// <summary>
    /// <c>schema.property-constraints</c> and <c>schema.property-format</c> find the bounds of
    /// <c>[Range]</c>, the lengths, the pattern and the format on the enum branch of each nullable enum
    /// property, an outer <c>allOf</c> of allowed values included: the build with validation reports
    /// nothing for the model.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ConstraintsOfANullableEnum_SatisfyTheConstraintAndFormatRules(OpenApiSpecVersion version)
    {
        OpenApiDocumentBuilder.BuildWithValidation(VersionedDocumentHarness.ModernApiOptions(version), new Core.Validation.ValidationContext(), out var result);

        result.Violations
            .Where(v => v.RuleId is "schema.property-constraints" or "schema.property-format"
                        && v.JsonPointer.Contains("NullableEnumRangeModel", StringComparison.Ordinal))
            .Should().BeEmpty();
    }
}
