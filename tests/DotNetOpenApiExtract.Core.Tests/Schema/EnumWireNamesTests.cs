using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using Xunit;
using NewtonsoftConverter = Newtonsoft.Json.Converters.StringEnumConverter;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi serialized into every version, built once for the class.</summary>
public sealed class EnumWireNamesFixture
{
    public EnumWireNamesFixture()
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
/// The values of a string enum are the members' names on the wire, by the converter in force
/// (property, then type, then global): System.Text.Json reads <c>[JsonStringEnumMemberName]</c>,
/// Newtonsoft.Json <c>[EnumMember(Value)]</c>. <c>x-enum-varnames</c> and <c>x-enum-descriptions</c>
/// keep the CLR members. The expected names are what the serializers really write.
/// </summary>
public class EnumWireNamesTests(EnumWireNamesFixture fixture) : IClassFixture<EnumWireNamesFixture>
{
    public enum Writer
    {
        /// <summary>System.Text.Json with the converter on the type.</summary>
        StjOnType,

        /// <summary>System.Text.Json with <c>JsonStringEnumConverter</c> in the options.</summary>
        StjConverter,

        /// <summary>Newtonsoft.Json with <c>StringEnumConverter</c>.</summary>
        Newtonsoft,
    }

    /// <summary>What <paramref name="writer"/> writes for every member of <typeparamref name="TEnum"/>, in member order.</summary>
    private static string[] Written<TEnum>(Writer writer) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => writer switch
        {
            Writer.StjOnType    => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(value))!,
            Writer.StjConverter => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(
                value, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }))!,
            _                   => JsonSerializer.Deserialize<string>(Newtonsoft.Json.JsonConvert.SerializeObject(value, new NewtonsoftConverter()))!,
        }).ToArray();

    private static string[] Written(Type enumType, Writer writer) => enumType.Name switch
    {
        nameof(Tint)           => Written<Tint>(writer),
        nameof(StjTint)        => Written<StjTint>(writer),
        nameof(GenericStjTint) => Written<GenericStjTint>(writer),
        _                      => throw new ArgumentOutOfRangeException(nameof(enumType)),
    };

    private JsonObject Property(OpenApiSpecVersion version, string name) =>
        fixture.Documents[version]["components"]!["schemas"]!["EnumWireNamesModel"]!["properties"]![name]!.AsObject();

    private static string[] Strings(JsonNode? array) =>
        array!.AsArray().Where(n => n != null).Select(n => n!.GetValue<string>()).ToArray();

    public static TheoryData<OpenApiSpecVersion, string, Type, Writer> ConverterCases()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, Type, Writer>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "typeConverter", typeof(StjTint), Writer.StjOnType);
            data.Add(version, "genericTypeConverter", typeof(GenericStjTint), Writer.StjOnType);
            data.Add(version, "nullableTypeConverter", typeof(StjTint), Writer.StjOnType);
            data.Add(version, "propertyStj", typeof(Tint), Writer.StjConverter);
            data.Add(version, "propertyNewtonsoft", typeof(Tint), Writer.Newtonsoft);
            data.Add(version, "propertyOverType", typeof(StjTint), Writer.Newtonsoft);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ConverterCases))]
    public void EnumValues_AreTheNamesTheConverterInForceWrites(OpenApiSpecVersion version, string property, Type enumType, Writer writer)
    {
        var expected = Written(enumType, writer);
        expected.Should().NotEqual(Enum.GetNames(enumType), because: "the fixture renames members for this writer");

        Strings(Property(version, property)["enum"]).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void BothAttributes_OnlyTheOneTheConverterReadsActs(OpenApiSpecVersion version)
    {
        // Crimson carries both attributes; each converter reads only its own.
        Strings(Property(version, "typeConverter")["enum"]).Should().Contain("scarlet").And.NotContain("crimson");
        Strings(Property(version, "propertyNewtonsoft")["enum"]).Should().Contain("crimson").And.NotContain("scarlet");
        Strings(Property(version, "propertyStj")["enum"]).Should().Contain("Green", because: "System.Text.Json ignores [EnumMember]");
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void VarnamesAndDescriptions_KeepTheClrMembers_ParallelToEnum(OpenApiSpecVersion version)
    {
        var schema = Property(version, "typeConverter");
        var values = Strings(schema["enum"]);

        Strings(schema["x-enum-varnames"]).Should().Equal(Enum.GetNames<StjTint>());
        Strings(schema["x-enum-descriptions"]).Should().Equal(
            "Renamed for System.Text.Json.", "Renamed for Newtonsoft.Json.", "Renamed for both serializers.", "Not renamed.");
        values.Should().HaveCount(Enum.GetNames<StjTint>().Length);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void WithoutConverter_TheEnumStaysNumeric(OpenApiSpecVersion version)
    {
        var schema = Property(version, "numeric");

        schema["type"]!.GetValue<string>().Should().Be("integer");
        schema["enum"]!.AsArray().Select(n => n!.GetValue<int>()).Should().Equal(0, 1, 2, 3);
        Strings(schema["x-enum-varnames"]).Should().Equal(Enum.GetNames<Tint>());
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void DefaultAndAllowedValues_UseTheSameWireNames(OpenApiSpecVersion version)
    {
        Property(version, "defaultedStj")["default"]!.GetValue<string>().Should().Be(Written<Tint>(Writer.StjConverter)[(int)Tint.Red]);
        Property(version, "defaultedByName")["default"]!.GetValue<string>().Should().Be(Written<StjTint>(Writer.StjOnType)[(int)StjTint.Crimson]);

        var allowed = Property(version, "allowedStj");
        var stj = Written<StjTint>(Writer.StjOnType);
        var constraint = allowed["allOf"]!.AsArray().Select(n => n!.AsObject()).Single(s => s["type"] == null && s["enum"] != null);
        Strings(constraint["enum"]).Should().Equal(stj[(int)StjTint.Red], stj[(int)StjTint.Crimson]);
        Strings(allowed["not"]!["enum"]).Should().Equal(stj[(int)StjTint.Green]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void BoundParameters_ListTheNamesModelBindingReads(OpenApiSpecVersion version)
    {
        // Route, query, header and form values are converted by the type converter, not by JSON.
        var parameters = fixture.Documents[version]["paths"]!["/keywords/enum-wire-names"]!["get"]!["parameters"]!.AsArray();
        var single = parameters.Single(p => p!["name"]!.GetValue<string>() == "tint")!["schema"]!.AsObject();
        var array = parameters.Single(p => p!["name"]!.GetValue<string>() == "tints")!["schema"]!["items"]!.AsObject();

        var converter = TypeDescriptor.GetConverter(typeof(StjTint));
        foreach (var schema in new[] { single, array })
        {
            var names = Strings(schema["enum"]);
            names.Should().Equal(Enum.GetNames<StjTint>());
            foreach (var name in names)
                converter.ConvertFrom(null, CultureInfo.InvariantCulture, name).Should().NotBeNull();
        }

        var wireOnly = Written<StjTint>(Writer.StjOnType)[(int)StjTint.Red];
        var bind = () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, wireOnly);
        bind.Should().Throw<FormatException>(because: "model binding does not read the JSON names");
        single["default"]!.GetValue<string>().Should().Be(nameof(StjTint.Red));
    }

    // ── Global converters: below the type's converter ─────────────────────────

    private static JsonObject Generate(string globalConverter, Type type)
    {
        var generator = new SchemaGenerator(new SchemaOptions { GlobalConverterTypeNames = [globalConverter] });
        var schema = generator.GenerateSchema(type);
        return JsonNode.Parse(schema.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None)
            .GetAwaiter().GetResult())!.AsObject();
    }

    [Fact]
    public void GlobalConverter_NamesAnEnumWithoutOneOfItsOwn_TheTypeConverterWins()
    {
        Strings(Generate(typeof(NewtonsoftConverter).FullName!, typeof(Tint))["enum"]).Should().Equal(Written<Tint>(Writer.Newtonsoft));
        Strings(Generate(typeof(JsonStringEnumConverter).FullName!, typeof(Tint))["enum"]).Should().Equal(Written<Tint>(Writer.StjConverter));
        Strings(Generate(typeof(NewtonsoftConverter).FullName!, typeof(StjTint))["enum"]).Should().Equal(Written<StjTint>(Writer.StjOnType));
    }
}
