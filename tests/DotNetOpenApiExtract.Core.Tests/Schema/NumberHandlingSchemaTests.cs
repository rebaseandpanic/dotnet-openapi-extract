using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using Microsoft.OpenApi;
using Xunit;
using CoreNumberHandling = DotNetOpenApiExtract.Core.JsonNumberHandling;
using StjNumberHandling = System.Text.Json.Serialization.JsonNumberHandling;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// The schema of a number under <c>JsonNumberHandling</c> is the union of what System.Text.Json writes
/// and what it reads: a numeric string branch for the string flags, a named-literal branch for
/// floating-point types; the property wins over the type, the type over the global option.
/// </summary>
public class NumberHandlingSchemaTests
{
    private const string IntegerPattern = "^[+-]?[0-9]+$";
    private const string FractionalPattern = "^[+-]?([0-9]+\\.?[0-9]*|\\.[0-9]+)([eE][+-]?[0-9]+)?$";

    /// <summary>Half is parsed with white space and group separators allowed (measured on STJ 10).</summary>
    private const string HalfPattern = "^\\s*[+-]?([0-9][0-9,]*\\.?[0-9]*|\\.[0-9]+)([eE][+-]?[0-9]+)?\\s*$";

    private static readonly Type[] NumericTypes = [typeof(int), typeof(long), typeof(uint), typeof(float), typeof(double), typeof(decimal), typeof(Half)];

    private static bool IsFloatingPoint(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(Half);

    public static TheoryData<Type, CoreNumberHandling> Cases
    {
        get
        {
            var data = new TheoryData<Type, CoreNumberHandling>();
            foreach (var type in NumericTypes)
            {
                foreach (var flags in new[]
                {
                    CoreNumberHandling.Strict,
                    CoreNumberHandling.AllowReadingFromString,
                    CoreNumberHandling.WriteAsString,
                    CoreNumberHandling.WriteAsString | CoreNumberHandling.AllowReadingFromString,
                    CoreNumberHandling.AllowNamedFloatingPointLiterals,
                })
                    data.Add(type, flags);
            }
            return data;
        }
    }

    private static IOpenApiSchema Generate(Type type, CoreNumberHandling flags, OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1) =>
        new SchemaGenerator(new SchemaOptions { NumberHandling = flags, OpenApiVersion = version }).GenerateSchema(type);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Schema_FollowsTheTableRow(Type type, CoreNumberHandling flags)
    {
        var schema = (OpenApiSchema)Generate(type, flags);
        var stringFlag = (flags & (CoreNumberHandling.AllowReadingFromString | CoreNumberHandling.WriteAsString)) != 0;
        var named = IsFloatingPoint(type) && flags != CoreNumberHandling.Strict;

        if (!stringFlag && !named)
        {
            schema.AnyOf.Should().BeNull();
            schema.Type.Should().Match(t => t == JsonSchemaType.Integer || t == JsonSchemaType.Number);
            return;
        }

        var branches = schema.AnyOf!.Cast<OpenApiSchema>().ToList();
        branches[0].Type.Should().Match(t => t == JsonSchemaType.Integer || t == JsonSchemaType.Number);
        branches.Should().HaveCount(1 + (stringFlag ? 1 : 0) + (named ? 1 : 0));

        if (stringFlag)
        {
            branches[1].Type.Should().Be(JsonSchemaType.String);
            branches[1].Pattern.Should().Be(
                type == typeof(int) || type == typeof(long) || type == typeof(uint) ? IntegerPattern
                : type == typeof(Half) ? HalfPattern
                : FractionalPattern);
        }

        if (named)
            branches[^1].Enum!.Select(e => e!.GetValue<string>()).Should().Equal("NaN", "Infinity", "-Infinity");
    }

    // ── Precedence: property > type > global ────────────────────────────────

    [JsonNumberHandling(StjNumberHandling.AllowReadingFromString)]
    public sealed class TypeLevelHandling
    {
        public int FromType { get; set; }

        [JsonNumberHandling(StjNumberHandling.Strict)]
        public int StrictProperty { get; set; }

        [JsonNumberHandling(StjNumberHandling.WriteAsString)]
        public double OwnFlags { get; set; }
    }

    public sealed class PropertyLevelHandling
    {
        public int FromGlobal { get; set; }

        [JsonNumberHandling(StjNumberHandling.Strict)]
        public int StrictProperty { get; set; }

        [JsonNumberHandling(StjNumberHandling.AllowReadingFromString)]
        public List<int> Items { get; set; } = [];

        [JsonNumberHandling(StjNumberHandling.AllowReadingFromString)]
        public int? Optional { get; set; }

        [JsonNumberHandling(StjNumberHandling.AllowReadingFromString)]
        [Range(1, 10)]
        public int Bounded { get; set; }
    }

    private static IDictionary<string, IOpenApiSchema> Properties(Type type, CoreNumberHandling? global, OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1)
    {
        var generator = new SchemaGenerator(new SchemaOptions { NumberHandling = global, OpenApiVersion = version });
        generator.GenerateSchema(type);
        return generator.Schemas[type.Name].Properties!;
    }

    [Fact]
    public void PropertyAttribute_WinsOverTypeAttribute_WhichWinsOverTheGlobalOption()
    {
        var properties = Properties(typeof(TypeLevelHandling), CoreNumberHandling.WriteAsString);

        ((OpenApiSchema)properties["fromType"]).AnyOf.Should().HaveCount(2, because: "the type's AllowReadingFromString applies");
        ((OpenApiSchema)properties["strictProperty"]).AnyOf.Should().BeNull(because: "Strict on the property wins");
        ((OpenApiSchema)properties["strictProperty"]).Type.Should().Be(JsonSchemaType.Integer);
        ((OpenApiSchema)properties["ownFlags"]).AnyOf.Should().HaveCount(3, because: "a double with a string flag has three branches");
    }

    [Fact]
    public void StrictProperty_UnderGlobalStringFlags_IsANumber()
    {
        var properties = Properties(typeof(PropertyLevelHandling), CoreNumberHandling.WriteAsString | CoreNumberHandling.AllowReadingFromString);

        ((OpenApiSchema)properties["fromGlobal"]).AnyOf.Should().HaveCount(2);
        ((OpenApiSchema)properties["strictProperty"]).AnyOf.Should().BeNull();
    }

    [Fact]
    public void Collections_GetTheRuleOnTheirItems()
    {
        var items = (OpenApiSchema)((OpenApiSchema)Properties(typeof(PropertyLevelHandling), null)["items"]).Items!;

        items.AnyOf.Should().HaveCount(2);
        ((OpenApiSchema)items.AnyOf![1]).Pattern.Should().Be(IntegerPattern);
    }

    [Fact]
    public void Ranges_ConstrainOnlyTheNumericBranch()
    {
        var bounded = (OpenApiSchema)Properties(typeof(PropertyLevelHandling), null)["bounded"];

        var number = (OpenApiSchema)bounded.AnyOf![0];
        number.Minimum.Should().Be("1");
        number.Maximum.Should().Be("10");
        bounded.Minimum.Should().BeNull();
        ((OpenApiSchema)bounded.AnyOf[1]).Minimum.Should().BeNull();
    }

    [Fact]
    public void Nullable_IsOnTheWholeUnion_ByVersion()
    {
        var v31 = (OpenApiSchema)Properties(typeof(PropertyLevelHandling), null, OpenApiSpecVersion.OpenApi3_1)["optional"];
        v31.Type.Should().BeNull();
        v31.AnyOf!.Cast<OpenApiSchema>().Should().Contain(b => b.Type == JsonSchemaType.Null);

        var v30 = (OpenApiSchema)Properties(typeof(PropertyLevelHandling), null, OpenApiSpecVersion.OpenApi3_0)["optional"];
        v30.Type.Should().Be(JsonSchemaType.Null, because: "3.0 writes the union itself as nullable: true");
        v30.AnyOf.Should().HaveCount(2);
    }

    // ── SC-003: what STJ writes and reads passes the schema ─────────────────

    private static SchemaConformance ConformanceFor(IOpenApiSchema root, OpenApiSpecVersion version)
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents { Schemas = new Dictionary<string, IOpenApiSchema> { ["Root"] = root } },
        };
        var json = document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult();
        return SchemaConformance.For(JsonNode.Parse(json)!);
    }

    private static readonly string[] Inputs =
        ["12", "\"12\"", "\"+12\"", "\"012\"", "\"-7\"", "\"1.5e3\"", "\"1E3\"", "\".5\"", "\"+.5\"", "\"1.\"", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"", "\" 12\"", "\"12 \"", "\"1,5\""];

    public static TheoryData<Type, CoreNumberHandling, OpenApiSpecVersion> WireCases
    {
        get
        {
            var data = new TheoryData<Type, CoreNumberHandling, OpenApiSpecVersion>();
            foreach (var row in Cases)
            {
                var (type, flags) = (row.Data.Item1, row.Data.Item2);
                foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
                    data.Add(type, flags, version);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(WireCases))]
    public void EverythingStjWritesOrReads_PassesTheSchema_AndASpacedStringFails(Type type, CoreNumberHandling flags, OpenApiSpecVersion version)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = (StjNumberHandling)(int)flags };
        var conformance = ConformanceFor(Generate(type, flags, version), version);

        foreach (var input in Inputs)
        {
            var accepted = StjWire.Accepts(input, type, options);
            var result = conformance.ValidateComponent("Root", JsonNode.Parse(input));
            if (accepted)
                result.IsValid.Should().BeTrue(because: $"STJ reads {input} as {type.Name} with {flags}: {string.Join("; ", result.Errors)}");
            if (input == "\" 12\"" && type != typeof(Half))
            {
                accepted.Should().BeFalse(because: "STJ rejects a space in a numeric string");
                result.IsValid.Should().BeFalse(because: "the grammar has no spaces");
            }
        }

        object[] values = type == typeof(Half)
            ? [(Half)1.5f, Half.NaN, Half.PositiveInfinity]
            : type == typeof(float) ? [1.5f, float.NaN, float.NegativeInfinity]
            : type == typeof(double) ? [1.5e3, double.NaN, double.PositiveInfinity]
            : type == typeof(decimal) ? [12.5m, -0.001m]
            : type == typeof(uint) ? [12u, uint.MaxValue]
            : type == typeof(long) ? [-12L, long.MaxValue]
            : [-12, int.MaxValue];
        foreach (var value in values)
        {
            string written;
            try
            {
                written = JsonSerializer.Serialize(value, type, options);
            }
            catch (Exception e) when (e is ArgumentException or JsonException)
            {
                continue; // NaN / Infinity without a flag that allows writing them: STJ refuses
            }

            var result = conformance.ValidateComponent("Root", JsonNode.Parse(written));
            result.IsValid.Should().BeTrue(because: $"STJ writes {written} for {type.Name} with {flags}: {string.Join("; ", result.Errors)}");
        }
    }

    // ── Allowed / denied values and ranges on the union ──────────────────────

    [JsonNumberHandling(StjNumberHandling.WriteAsString | StjNumberHandling.AllowReadingFromString)]
    public sealed class ValuesOnUnions
    {
        [AllowedValues(1, 2)] public int Level { get; set; } = 1;

        [DeniedValues(3)] public int NotThree { get; set; } = 4;

        [AllowedValues(true)] public int Mismatch { get; set; }

        [AllowedValues(1.5, 2.5)] public double Ratio { get; set; } = 1.5;

        [Range(1, 10)] public int? Bounded { get; set; } = 5;

        [AllowedValues(1, 2)] public int? OptionalLevel { get; set; } = 2;
    }

    private static (IDictionary<string, IOpenApiSchema> Properties, List<DotNetOpenApiExtract.Core.Diagnostics.ExtractionDiagnostic> Diagnostics, SchemaGenerator Generator)
        Values(OpenApiSpecVersion version)
    {
        var diagnostics = new List<DotNetOpenApiExtract.Core.Diagnostics.ExtractionDiagnostic>();
        var generator = new SchemaGenerator(new SchemaOptions { OpenApiVersion = version, OnDiagnostic = diagnostics.Add });
        generator.GenerateSchema(typeof(ValuesOnUnions));
        return (generator.Schemas[nameof(ValuesOnUnions)].Properties!, diagnostics, generator);
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions =>
        [OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void AllowedValues_ConstrainEachBranchWithItsOwnJsonType(OpenApiSpecVersion version)
    {
        var level = (OpenApiSchema)Values(version).Properties["level"];

        level.Enum.Should().BeNull(because: "a numeric enum outside the union would reject the string STJ writes");
        var number = (OpenApiSchema)level.AnyOf![0];
        var text = (OpenApiSchema)level.AnyOf[1];
        number.Enum!.Select(e => e!.ToJsonString()).Should().Equal("1", "2");
        text.Enum.Should().BeNull(because: "STJ reads other spellings of an allowed number; the string branch keeps its grammar");
        text.Pattern.Should().Be(IntegerPattern);

        var notThree = (OpenApiSchema)Values(version).Properties["notThree"];
        ((OpenApiSchema)notThree.AnyOf![0]).Not!.Enum!.Select(e => e!.ToJsonString()).Should().Equal(["3"]);
        ((OpenApiSchema)notThree.AnyOf[1]).Not!.Enum!.Select(e => e!.GetValue<string>()).Should().Equal(["3"]);

        var ratio = (OpenApiSchema)Values(version).Properties["ratio"];
        ratio.AnyOf.Should().HaveCount(2, because: "the allowed finite numbers exclude the NaN / Infinity branch");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ValueOfAnotherJsonType_OnAUnion_IsReported(OpenApiSpecVersion version)
    {
        var (properties, diagnostics, _) = Values(version);

        ((OpenApiSchema)((OpenApiSchema)properties["mismatch"]).AnyOf![0]).Enum.Should().BeNull();
        diagnostics.Should().ContainSingle(d => d.Code == DotNetOpenApiExtract.Core.Diagnostics.ExtractionDiagnosticCodes.SchemaValueNotConvertible);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NullableUnion_KeepsRangeAndValuesOnTheNumericBranch(OpenApiSpecVersion version)
    {
        var (properties, diagnostics, _) = Values(version);

        var bounded = (OpenApiSchema)properties["bounded"];
        var number = (OpenApiSchema)bounded.AnyOf![0];
        number.Minimum.Should().Be("1");
        number.Maximum.Should().Be("10");

        var optional = (OpenApiSchema)properties["optionalLevel"];
        ((OpenApiSchema)optional.AnyOf![0]).Enum!.Select(e => e!.ToJsonString()).Should().Equal("1", "2");
        diagnostics.Where(d => d.Subjects.Any(s => s.EndsWith(".OptionalLevel", StringComparison.Ordinal))).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void ObjectWrittenByStj_PassesTheConstrainedUnions_AndAForbiddenStringFails(OpenApiSpecVersion version)
    {
        var (_, _, generator) = Values(version);
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                Schemas = generator.Schemas.ToDictionary(p => p.Key, p => (IOpenApiSchema)p.Value),
            },
        };
        var conformance = SchemaConformance.For(JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!);

        var written = JsonNode.Parse(JsonSerializer.Serialize(new ValuesOnUnions(), StjWire.Mvc()))!;
        written["level"]!.GetValue<string>().Should().Be("1", because: "WriteAsString writes the number as a string");
        var result = conformance.ValidateComponent(nameof(ValuesOnUnions), written);
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));

        StjWire.Accepts("{\"level\":\"+1\"}", typeof(ValuesOnUnions), StjWire.Mvc()).Should().BeTrue();
        written["level"] = "+1";
        conformance.ValidateComponent(nameof(ValuesOnUnions), written).IsValid
            .Should().BeTrue(because: "a spelling STJ reads passes the schema");
        written["level"] = 3;
        conformance.ValidateComponent(nameof(ValuesOnUnions), written).IsValid
            .Should().BeFalse(because: "3 is not an allowed number");
        written["level"] = "1";
        written["notThree"] = "3";
        conformance.ValidateComponent(nameof(ValuesOnUnions), written).IsValid.Should().BeFalse();
    }
}
