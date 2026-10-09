using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Annotations;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi serialized into every version, built once for the class.</summary>
public sealed class StandardAnnotationFixture
{
    public StandardAnnotationFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = VersionedDocumentHarness.Build(VersionedDocumentHarness.ModernApiOptions(version));
            Documents[version] = JsonNode.Parse(
                document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];
}

/// <summary>
/// Standard annotation attributes the extractor reads in every version: <c>[Length]</c>,
/// <c>[DataType]</c> and the single-winner format priority, <c>[Display(Description)]</c> in the
/// description chain, <c>readOnly</c> / <c>writeOnly</c> / <c>title</c>, and the extraction error for a
/// property that ends up both read-only and write-only.
/// </summary>
public class StandardAnnotationAttributesTests(StandardAnnotationFixture fixture) : IClassFixture<StandardAnnotationFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Component(OpenApiSpecVersion version, string id) =>
        fixture.Documents[version]["components"]!["schemas"]![id]!.AsObject();

    private JsonObject Property(OpenApiSpecVersion version, string model, string name) =>
        Component(version, model)["properties"]![name]!.AsObject();

    private static string? Text(JsonObject schema, string keyword) => schema[keyword]?.GetValue<string>();

    // ── [Length] ─────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Length_ConstrainsStringsCollectionsAndDictionariesByTheirOwnKeywords(OpenApiSpecVersion version)
    {
        static Dictionary<string, int> Bounds(JsonObject schema) => schema
            .Where(p => p.Key.StartsWith("min", StringComparison.Ordinal) || p.Key.StartsWith("max", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value!.GetValue<int>());

        Bounds(Property(version, "LengthModel", "code")).Should().Equal(new Dictionary<string, int> { ["minLength"] = 2, ["maxLength"] = 8 });
        Bounds(Property(version, "LengthModel", "tags")).Should().Equal(new Dictionary<string, int> { ["minItems"] = 1, ["maxItems"] = 3 });
        Bounds(Property(version, "LengthModel", "numbers")).Should().Equal(new Dictionary<string, int> { ["minItems"] = 2, ["maxItems"] = 5 });
        Bounds(Property(version, "LengthModel", "counts")).Should().Equal(new Dictionary<string, int> { ["minProperties"] = 1, ["maxProperties"] = 4 });
        Bounds(Property(version, "LengthModel", "empty")).Should().Equal(new Dictionary<string, int> { ["maxLength"] = 0 },
            because: "a zero maximum admits only the empty string; a zero minimum constrains nothing");
    }

    // ── [DataType] → format ──────────────────────────────────────────────────

    /// <summary>The members of the table that give a format; every other member gives none.</summary>
    private static readonly Dictionary<DataType, string> DataTypeFormats = new()
    {
        [DataType.DateTime]     = "date-time",
        [DataType.Date]         = "date",
        [DataType.Time]         = "time",
        [DataType.Duration]     = "duration",
        [DataType.EmailAddress] = "email",
        [DataType.Password]     = "password",
        [DataType.Url]          = "uri",
        [DataType.ImageUrl]     = "uri",
        [DataType.PhoneNumber]  = "phone",
        [DataType.Upload]       = "binary",
    };

    public static TheoryData<OpenApiSpecVersion, DataType> EveryDataTypeMember()
    {
        var data = new TheoryData<OpenApiSpecVersion, DataType>();
        foreach (var version in VersionedDocumentHarness.Versions)
        foreach (var member in Enum.GetValues<DataType>())
            data.Add(version, member);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryDataTypeMember))]
    public void DataType_GivesTheFormatOfTheTable_AndAMemberWithoutOneKeepsTheTypeFormat(OpenApiSpecVersion version, DataType member)
    {
        // Every property of the fixture is a Guid: the type's own format is uuid.
        var schema = Property(version, "DataTypeModel", "as" + member);

        Text(schema, "format").Should().Be(DataTypeFormats.GetValueOrDefault(member, "uuid"));
        Text(schema, "type").Should().Be("string");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void DataType_CustomNameGivesNoFormat_AndATableMemberFormatsAString(OpenApiSpecVersion version)
    {
        Text(Property(version, "DataTypeModel", "asCustomName"), "format").Should().Be("uuid");
        Text(Property(version, "DataTypeModel", "dateText"), "format").Should().Be("date");
    }

    // ── Format priority ──────────────────────────────────────────────────────

    public static TheoryData<OpenApiSpecVersion, string, string> FormatWinners()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "schemaOverProfile", "x-code");
            data.Add(version, "schemaOverDataType", "x-code");
            data.Add(version, "schemaOverType", "x-code");
            data.Add(version, "emailOverDataType", "email");
            data.Add(version, "urlOverDataType", "uri");
            data.Add(version, "phoneOverDataType", "phone");
            data.Add(version, "profileOverType", "email");
            data.Add(version, "dataTypeOverType", "date");
            data.Add(version, "formatlessDataTypeWithProfile", "phone");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(FormatWinners))]
    public void Format_HasOneWinnerByPriority(OpenApiSpecVersion version, string property, string winner)
    {
        var schema = Property(version, "FormatPriorityModel", property);

        Text(schema, "format").Should().Be(winner);
        schema.ToJsonString().Split("\"format\"").Length.Should().Be(2, because: "a single format is written");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Format_OnANumberReadFromStrings_GoesOnTheNumericBranch(OpenApiSpecVersion version)
    {
        var declared = Property(version, "FormatPriorityModel", "schemaOverNumberBranch");
        Text(declared, "format").Should().BeNull();
        var branches = declared["anyOf"]!.AsArray().Select(b => b!.AsObject()).ToList();
        Text(branches[0], "format").Should().Be("x-count", because: "the declared format replaces int32 on the number");
        branches.Skip(1).Should().OnlyContain(b => b["format"] == null);

        var formatless = Property(version, "FormatPriorityModel", "formatlessDataTypeOnNumberBranch");
        Text(formatless["anyOf"]![0]!.AsObject(), "format").Should().Be("double",
            because: "a member without a format leaves the type's format");
    }

    // ── [Display(Description)] ───────────────────────────────────────────────

    public static TheoryData<OpenApiSpecVersion, string, string> DescriptionWinners()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "schemaOverDisplay", "From SwaggerSchema");
            data.Add(version, "schemaConstructorOverDisplay", "From the SwaggerSchema constructor");
            data.Add(version, "descriptionOverDisplay", "From Description");
            data.Add(version, "displayOverXml", "From Display");
            data.Add(version, "displayWithoutDescription", "Xml summary.");
            data.Add(version, "displayResourceKey", "Xml summary.");
            data.Add(version, "displayOnNullableNumber", "From Display");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(DescriptionWinners))]
    public void Display_IsBelowSwaggerSchemaAndDescription_AndAboveXml(OpenApiSpecVersion version, string property, string winner) =>
        Text(Property(version, "DescriptionPriorityModel", property), "description").Should().Be(winner);

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Display_OnAReference_GoesOnTheWrapper_AndLeavesTheComponent(OpenApiSpecVersion version)
    {
        var wrapper = Property(version, "DescriptionPriorityModel", "displayOnReference");

        Text(wrapper, "description").Should().Be("From Display");
        wrapper["allOf"]![0]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/AnnotatedTarget");
        Text(Component(version, "AnnotatedTarget"), "description").Should().Be("A nested object for reference-typed annotated properties.");
    }

    // ── readOnly / writeOnly / title ─────────────────────────────────────────

    public static TheoryData<OpenApiSpecVersion, string, bool, bool> AccessCases()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, bool, bool>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "schemaReadOnly", true, false);
            data.Add(version, "attributeReadOnly", true, false);
            data.Add(version, "schemaWriteOnly", false, true);
            data.Add(version, "explicitFalseOverAttribute", false, false);
            data.Add(version, "attributeFalse", false, false);
            data.Add(version, "writeOnlyOverAttribute", false, true);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AccessCases))]
    public void ReadOnlyAndWriteOnly_FollowTheEffectiveValue(OpenApiSpecVersion version, string property, bool readOnly, bool writeOnly)
    {
        var schema = Property(version, "AccessModel", property);

        // Only a true value is written.
        schema["readOnly"]?.GetValue<bool>().Should().BeTrue();
        schema["writeOnly"]?.GetValue<bool>().Should().BeTrue();
        schema.ContainsKey("readOnly").Should().Be(readOnly);
        schema.ContainsKey("writeOnly").Should().Be(writeOnly);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Title_IsWritten_AndAccessOnAReferenceGoesOnTheWrapper(OpenApiSpecVersion version)
    {
        Text(Property(version, "AccessModel", "titled"), "title").Should().Be("Display title");

        var wrapper = Property(version, "AccessModel", "readOnlyReference");
        wrapper["readOnly"]!.GetValue<bool>().Should().BeTrue();
        Text(wrapper, "title").Should().Be("Target");
        wrapper["allOf"]![0]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/AnnotatedTarget");
        Component(version, "AnnotatedTarget").ContainsKey("readOnly").Should().BeFalse();
        Component(version, "AnnotatedTarget").ContainsKey("title").Should().BeFalse();
    }

    // ── Both readOnly and writeOnly: extraction error ────────────────────────

    public sealed class SchemaBoth { [SwaggerSchema(ReadOnly = true, WriteOnly = true)] public string Value { get; set; } = ""; }

    public sealed class AttributeAndSchemaWriteOnly { [ReadOnly(true), SwaggerSchema(WriteOnly = true)] public string Value { get; set; } = ""; }

    public sealed class BothOnReference { [SwaggerSchema(ReadOnly = true, WriteOnly = true)] public AttributeOverruled? Value { get; set; } }

    public sealed class AttributeOverruled { [ReadOnly(true), SwaggerSchema(ReadOnly = false, WriteOnly = true)] public string Value { get; set; } = ""; }

    public sealed class AttributeFalseWithWriteOnly { [ReadOnly(false), SwaggerSchema(WriteOnly = true)] public string Value { get; set; } = ""; }

    public static TheoryData<Type, bool> AccessDeclarations => new()
    {
        { typeof(SchemaBoth), true },
        { typeof(AttributeAndSchemaWriteOnly), true },
        { typeof(BothOnReference), true },
        { typeof(AttributeOverruled), false },
        { typeof(AttributeFalseWithWriteOnly), false },
    };

    [Theory]
    [MemberData(nameof(AccessDeclarations))]
    public void ExtractionError_ExactlyWhenTheEffectiveReadOnlyAndWriteOnlyAreBothTrue(Type type, bool rejected)
    {
        var generate = () => new SchemaGenerator().GenerateSchema(type);

        if (rejected)
        {
            var error = generate.Should().Throw<OpenApiExtractionException>().Which;
            error.TypeName.Should().Be(type.FullName);
            error.MemberName.Should().Be("Value");
        }
        else
        {
            generate.Should().NotThrow();
        }
    }
}
