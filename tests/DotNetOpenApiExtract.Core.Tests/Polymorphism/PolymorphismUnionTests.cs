using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Polymorphism;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Polymorphism;

/// <summary>Builds ModernApi once per target version for the whole class.</summary>
public sealed class PolymorphismFixture
{
    public PolymorphismFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Documents[version] = VersionedDocumentHarness.SerializeAsync(
                    document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Diagnostics[version] = diagnostics;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];
}

/// <summary>
/// An abstract base or an interface whose every derived type has a discriminator value is a union:
/// <c>oneOf</c> of variants plus a <c>discriminator</c>; a variant carries the discriminator property
/// with its value, the derived type's direct-use schema does not.
/// </summary>
public class PolymorphismUnionTests(PolymorphismFixture fixture) : IClassFixture<PolymorphismFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";

    public static TheoryData<OpenApiSpecVersion> Versions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Schemas(OpenApiSpecVersion version) =>
        fixture.Documents[version]["components"]!["schemas"]!.AsObject();

    private string ResponseComponent(OpenApiSpecVersion version, string path)
    {
        var reference = fixture.Documents[version]["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!
            ["application/json"]!["schema"]!["$ref"]!.GetValue<string>();
        return Id(reference);
    }

    private static string Id(string reference)
    {
        reference.Should().StartWith(SchemasPrefix);
        return reference[SchemasPrefix.Length..];
    }

    /// <summary>The single allowed value of a discriminator property schema, by the form of the version.</summary>
    private static JsonNode SingleValue(JsonNode property, OpenApiSpecVersion version, bool isString)
    {
        if (version == OpenApiSpecVersion.OpenApi3_0 || !isString)
        {
            property.AsObject().ContainsKey("const").Should().BeFalse();
            var values = property["enum"]!.AsArray();
            values.Should().ContainSingle();
            return values[0]!;
        }

        property.AsObject().ContainsKey("enum").Should().BeFalse();
        return property["const"]!;
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void AbstractBase_IsOneOfVariantsWithDiscriminator(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);
        var union = schemas[ResponseComponent(version, "/polymorphism/animal")]!;

        union["discriminator"]!["propertyName"]!.GetValue<string>().Should().Be("$type");
        union["discriminator"]!.AsObject().ContainsKey("defaultMapping").Should().BeFalse();
        var mapping = union["discriminator"]!["mapping"]!.AsObject();
        mapping.Select(m => m.Key).Should().BeEquivalentTo(["cat", "dog"]);

        var alternatives = union["oneOf"]!.AsArray().Select(a => Id(a!["$ref"]!.GetValue<string>())).ToList();
        alternatives.Should().HaveCount(2);
        alternatives.Should().BeEquivalentTo(mapping.Select(m => Id(m.Value!.GetValue<string>())));

        foreach (var (value, ownProperty) in new[] { ("cat", "lives"), ("dog", "breed") })
        {
            var variant = schemas[Id(mapping[value]!.GetValue<string>())]!;
            variant["required"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Contain("$type");
            var discriminator = variant["properties"]!["$type"]!;
            discriminator["type"]!.GetValue<string>().Should().Be("string");
            SingleValue(discriminator, version, isString: true).GetValue<string>().Should().Be(value);
            variant["properties"]!.AsObject().ContainsKey(ownProperty).Should().BeTrue();
            variant["properties"]!.AsObject().ContainsKey("name").Should().BeTrue(because: "base properties are flattened in");
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void DirectUseOfDerivedType_HasNoDiscriminator(OpenApiSpecVersion version)
    {
        var cat = Schemas(version)[ResponseComponent(version, "/polymorphism/cat")]!;

        cat["properties"]!.AsObject().ContainsKey("$type").Should().BeFalse();
        (cat["required"]?.AsArray().Any(r => r!.GetValue<string>() == "$type") ?? false).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void Interface_IntegerValuesAndCustomPropertyName(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);
        var union = schemas[ResponseComponent(version, "/polymorphism/shape")]!;

        union["discriminator"]!["propertyName"]!.GetValue<string>().Should().Be("kind");
        var mapping = union["discriminator"]!["mapping"]!.AsObject();
        mapping.Select(m => m.Key).Should().BeEquivalentTo(["1", "2"]);

        var circle = schemas[Id(mapping["1"]!.GetValue<string>())]!;
        circle["properties"]!["kind"]!["type"]!.GetValue<string>().Should().Be("integer");
        var value = SingleValue(circle["properties"]!["kind"]!, version, isString: false);
        value.GetValueKind().Should().Be(JsonValueKind.Number);
        value.GetValue<int>().Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void SwashbuckleAttributes_GiveTheSameUnion(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);
        var union = schemas[ResponseComponent(version, "/polymorphism/vehicle")]!;

        union["discriminator"]!["propertyName"]!.GetValue<string>().Should().Be("vehicleType");
        var mapping = union["discriminator"]!["mapping"]!.AsObject();
        mapping.Select(m => m.Key).Should().BeEquivalentTo(["car", "bike"]);
        var car = schemas[Id(mapping["car"]!.GetValue<string>())]!;
        SingleValue(car["properties"]!["vehicleType"]!, version, isString: true).GetValue<string>().Should().Be("car");
        car["properties"]!.AsObject().ContainsKey("seats").Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void StjAndSwashbuckle_StjDefinesTheUnion(OpenApiSpecVersion version)
    {
        var union = Schemas(version)[ResponseComponent(version, "/polymorphism/payment")]!;

        union["discriminator"]!["propertyName"]!.GetValue<string>().Should().Be("$type");
        union["discriminator"]!["mapping"]!.AsObject().Select(m => m.Key).Should().BeEquivalentTo(["card", "cash"]);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void ConverterOnBase_NoUnion(OpenApiSpecVersion version)
    {
        var document = Schemas(version)[ResponseComponent(version, "/polymorphism/document")]!.AsObject();

        document.ContainsKey("oneOf").Should().BeFalse();
        document.ContainsKey("anyOf").Should().BeFalse();
        document.ContainsKey("discriminator").Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void DtoNamedLikeAVariant_KeepsItsComponent_MappingLeadsToTheVariant(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);
        var dto = schemas[ResponseComponent(version, "/polymorphism/cat-as-animal")]!;
        var union = schemas[ResponseComponent(version, "/polymorphism/animal")]!;
        var catVariant = schemas[Id(union["discriminator"]!["mapping"]!["cat"]!.GetValue<string>())]!;

        dto["properties"]!.AsObject().Select(p => p.Key).Should().Equal("adoptedBy");
        catVariant["properties"]!.AsObject().ContainsKey("$type").Should().BeTrue();
        catVariant["properties"]!.AsObject().ContainsKey("lives").Should().BeTrue();
    }

    // ── Direct schema generation ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void SchemaGenerator_WritesTheSameUnion(OpenApiSpecVersion version)
    {
        var generator = new SchemaGenerator(new SchemaOptions { OpenApiVersion = version });

        var reference = generator.GenerateSchema(typeof(Animal)) as OpenApiSchemaReference;

        reference.Should().NotBeNull();
        var union = (OpenApiSchema)generator.Schemas[reference!.Reference.Id!];
        union.OneOf.Should().HaveCount(2);
        union.Discriminator!.PropertyName.Should().Be("$type");
        union.Discriminator.Mapping!.Keys.Should().BeEquivalentTo(["cat", "dog"]);
        var cat = (OpenApiSchema)generator.Schemas[union.Discriminator.Mapping["cat"].Reference.Id!];
        cat.Required.Should().Contain("$type");
    }

    // ── Provably wrong contract ──────────────────────────────────────────────

    [JsonDerivedType(typeof(ClashingDerived), "clash")]
    public abstract class ClashingBase
    {
        public string Id { get; set; } = string.Empty;
    }

    public sealed class ClashingDerived : ClashingBase
    {
        [JsonPropertyName("$type")]
        public string Kind { get; set; } = string.Empty;
    }

    [Fact]
    public void DerivedPropertyNamedLikeTheDiscriminator_IsAnExtractionError()
    {
        // The oracle: System.Text.Json itself rejects this contract.
        var serialize = () => JsonSerializer.Serialize<ClashingBase>(new ClashingDerived());
        serialize.Should().Throw<InvalidOperationException>();

        var generate = () => new SchemaGenerator().GenerateSchema(typeof(ClashingBase));

        var error = generate.Should().ThrowExactly<OpenApiExtractionException>().Which;
        error.TypeName.Should().Be(typeof(ClashingDerived).FullName);
        error.MemberName.Should().Be(nameof(ClashingDerived.Kind));
    }
}
