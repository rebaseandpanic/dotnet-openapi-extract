using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Polymorphism;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Conformance;

/// <summary>Builds ModernApi for 3.1 and 3.2 once and prepares schema validation for each.</summary>
public sealed class PolymorphismConformanceFixture
{
    public PolymorphismConformanceFixture()
    {
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
        {
            var document = VersionedDocumentHarness.BuildAndSerializeAsync(
                    VersionedDocumentHarness.ModernApiOptions(version), DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Documents[version] = document;
            Conformance[version] = SchemaConformance.For(document);
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, SchemaConformance> Conformance { get; } = [];
}

/// <summary>
/// The generated polymorphic schemas describe what System.Text.Json really writes and reads:
/// objects STJ writes pass the union (with exactly one <c>oneOf</c> match), and for every input the
/// schema accepts it exactly when <c>JsonSerializer.Deserialize</c> does.
/// </summary>
public class PolymorphismConformanceTests(PolymorphismConformanceFixture fixture) : IClassFixture<PolymorphismConformanceFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";
    private static readonly JsonSerializerOptions Wire = StjWire.Mvc();

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    private string Component(OpenApiSpecVersion version, string path)
    {
        var reference = fixture.Documents[version]["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!
            ["application/json"]!["schema"]!["$ref"]!.GetValue<string>();
        reference.Should().StartWith(SchemasPrefix);
        return reference[SchemasPrefix.Length..];
    }

    // ── Objects written by STJ ───────────────────────────────────────────────

    public static TheoryData<OpenApiSpecVersion, string, object, Type> WrittenPolymorphically
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, object, Type>();
            foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
            {
                data.Add(version, "/polymorphism/animal", new Cat { Name = "Tom", Lives = 9 }, typeof(Animal));
                data.Add(version, "/polymorphism/animal", new Dog { Name = "Rex", Breed = "Collie" }, typeof(Animal));
                data.Add(version, "/polymorphism/shape", new Circle { Radius = 2 }, typeof(IShape));
                data.Add(version, "/polymorphism/payment", new CardPayment { Amount = 5, Card = "****1234" }, typeof(Payment));
                data.Add(version, "/polymorphism-alt/pet", new Hamster { Nickname = "Ham", WheelSize = 12 }, typeof(Pet));
                data.Add(version, "/polymorphism-alt/pet", new Pet { Nickname = "Generic" }, typeof(Pet));
                data.Add(version, "/polymorphism-alt/fish", new Shark { Fins = 5, Teeth = 300 }, typeof(Fish));
                data.Add(version, "/polymorphism-alt/fish", new Fish { Fins = 2 }, typeof(Fish));
                data.Add(version, "/polymorphism-alt/sensor", new Sensor { SerialNumber = "s" }, typeof(Sensor));
                data.Add(version, "/polymorphism-alt/sensor", new Thermometer { SerialNumber = "t", Celsius = 20 }, typeof(Sensor));
                data.Add(version, "/polymorphism-alt/tree",
                    new Branch { Label = "root", Children = [new Leaf { Label = "l", Value = 1 }] }, typeof(TreeNode));
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(WrittenPolymorphically))]
    public void WrittenThroughTheBase_PassesTheUnionWithOneMatch(OpenApiSpecVersion version, string path, object value, Type declared)
    {
        var instance = StjWire.Serialize(value, declared, Wire);
        var union = Component(version, path);
        var conformance = fixture.Conformance[version];

        var result = conformance.ValidateComponent(union, instance);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        conformance.CountMatchingOneOfBranches(union, instance).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void DerivedWrittenDirectly_PassesTheDirectUseSchema(OpenApiSpecVersion version)
    {
        var instance = StjWire.Serialize(new Cat { Name = "Tom", Lives = 9 }, Wire);
        instance!.AsObject().ContainsKey("$type").Should().BeFalse(because: "STJ writes no discriminator for direct use");

        fixture.Conformance[version].ValidateComponent(Component(version, "/polymorphism/cat"), instance)
            .IsValid.Should().BeTrue();
    }

    // ── Concrete base: the schema agrees with STJ on every input ─────────────

    public static TheoryData<OpenApiSpecVersion, string, Type, string> ConcreteBaseInputs
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, Type, string>();
            foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
            {
                // IgnoreUnrecognizedTypeDiscriminators = false
                data.Add(version, "/polymorphism-alt/pet", typeof(Pet), """{"$type":"hamster","nickname":"Ham","wheelSize":12}""");
                data.Add(version, "/polymorphism-alt/pet", typeof(Pet), """{"nickname":"Plain"}""");
                data.Add(version, "/polymorphism-alt/pet", typeof(Pet), """{"$type":"pet","nickname":"Declared"}""");
                data.Add(version, "/polymorphism-alt/pet", typeof(Pet), """{"$type":"unicorn","nickname":"Unknown"}""");
                // IgnoreUnrecognizedTypeDiscriminators = true
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":"shark","fins":5,"teeth":300}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"fins":2}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":"whale","fins":2}""");
                // What STJ reads as a discriminator: a string or an Int32 integer, nothing else.
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":7,"fins":2}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":true,"fins":2}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":null,"fins":2}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":1.5,"fins":2}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":{},"fins":2}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":[],"fins":2}""");
                data.Add(version, "/polymorphism-alt/fish", typeof(Fish), """{"$type":2147483648,"fins":2}""");
                data.Add(version, "/polymorphism-alt/pet", typeof(Pet), """{"$type":true,"nickname":"x"}""");
                data.Add(version, "/polymorphism-alt/pet", typeof(Pet), """{"$type":null,"nickname":"x"}""");
                // The base listed without a value: unknown values are rejected.
                data.Add(version, "/polymorphism-alt/sensor", typeof(Sensor), """{"serialNumber":"s"}""");
                data.Add(version, "/polymorphism-alt/sensor", typeof(Sensor), """{"$type":"thermo","serialNumber":"s","celsius":1}""");
                data.Add(version, "/polymorphism-alt/sensor", typeof(Sensor), """{"$type":"zz","serialNumber":"s"}""");
                // Integer values, unknown values read as the base: mapping is type-sensitive.
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":1,"carats":2,"depth":3}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":2,"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":"1","carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":"x","carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":2147483647,"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":2147483648,"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":-2147483649,"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":true,"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":null,"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":1.5,"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":{},"carats":2}""");
                data.Add(version, "/polymorphism-alt/gem", typeof(Gem), """{"$type":[],"carats":2}""");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ConcreteBaseInputs))]
    public void ConcreteBase_SchemaAcceptsExactlyWhatStjReads(OpenApiSpecVersion version, string path, Type baseType, string json)
    {
        var stjAccepts = StjWire.Accepts(json, baseType, Wire);
        var instance = JsonNode.Parse(json);
        var union = Component(version, path);
        var conformance = fixture.Conformance[version];

        var schemaAccepts = conformance.ValidateComponent(union, instance).IsValid;

        schemaAccepts.Should().Be(stjAccepts);
        conformance.CountMatchingOneOfBranches(union, instance).Should().Be(stjAccepts ? 1 : 0);
    }

    [Fact]
    public void ConcreteBase_InputsCoverBothOutcomes()
    {
        // Guard for the test above: the unknown value is rejected by STJ for Pet and read for Fish.
        StjWire.Accepts("""{"$type":"unicorn","nickname":"Unknown"}""", typeof(Pet), Wire).Should().BeFalse();
        StjWire.Accepts("""{"$type":"whale","fins":2}""", typeof(Fish), Wire).Should().BeTrue();
        StjWire.Accepts("""{"$type":true,"fins":2}""", typeof(Fish), Wire).Should().BeFalse();
        StjWire.Accepts("""{"$type":"1","carats":2}""", typeof(Gem), Wire).Should().BeTrue();
        StjWire.Accepts("""{"$type":2147483648,"carats":2}""", typeof(Gem), Wire).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void ValueLessDerivedThatIsABaseItself_WrittenFlat_PassesTheUnion(OpenApiSpecVersion version)
    {
        var conformance = fixture.Conformance[version];
        var union = Component(version, "/polymorphism-alt/household");

        foreach (var value in new Household[] { new Flat { Rooms = 3, Floor = 2 }, new Household { Rooms = 1 } })
        {
            var instance = StjWire.Serialize(value, typeof(Household), Wire);
            instance!.AsObject().ContainsKey("$type").Should().BeFalse(because: "STJ writes no discriminator here");
            var result = conformance.ValidateComponent(union, instance);
            result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        }
    }

    // ── anyOf union ──────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void AnyOfUnion_AcceptsWhatStjWrites_VariantRejectsAForeignValue(OpenApiSpecVersion version)
    {
        var conformance = fixture.Conformance[version];
        var unionId = Component(version, "/polymorphism-excluded/message");

        foreach (var value in new Message[] { new TextMessage { Sender = "a", Text = "hi" }, new ImageMessage { Sender = "b", Url = "u" } })
        {
            var instance = StjWire.Serialize(value, typeof(Message), Wire);
            var result = conformance.ValidateComponent(unionId, instance);
            result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        }

        const string foreign = """{"$type":"video","sender":"c"}""";
        StjWire.Accepts(foreign, typeof(Message), Wire).Should().BeFalse();

        var schemas = fixture.Documents[version]["components"]!["schemas"]!.AsObject();
        var variants = schemas[unionId]!["anyOf"]!.AsArray()
            .Select(a => a!["$ref"]!.GetValue<string>()[SchemasPrefix.Length..])
            .Where(id => schemas[id]!["required"]?.AsArray().Any(r => r!.GetValue<string>() == "$type") ?? false)
            .ToList();
        variants.Should().NotBeEmpty();
        variants.Should().AllSatisfy(id => conformance.ValidateComponent(id, JsonNode.Parse(foreign)).IsValid.Should().BeFalse());
    }
}
