using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Polymorphism;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Polymorphism;

/// <summary>Builds ModernApi per target version, and for 3.0 with the excluded prefix, for the whole class.</summary>
public sealed class PolymorphismAlternativesFixture
{
    public const string ExcludedPrefix = "/polymorphism-excluded";

    public PolymorphismAlternativesFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = Build(version, null);
            Documents[version] = VersionedDocumentHarness.SerializeAsync(
                    document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Diagnostics[version] = diagnostics;
        }

        Excluded30 = Build(OpenApiSpecVersion.OpenApi3_0, [ExcludedPrefix]).Diagnostics;
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];

    public IReadOnlyList<ExtractionDiagnostic> Excluded30 { get; }

    private static (OpenApiDocument Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(
        OpenApiSpecVersion version, IReadOnlyList<string>? excluded) =>
        VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            OpenApiVersion      = version,
            ExcludePathPrefixes = excluded,
            OnDiagnostic        = onDiagnostic,
        });
}

/// <summary>
/// Union alternatives beyond the abstract case: the base branch of a concrete base, <c>anyOf</c>
/// when a derived type has no value, nested and recursive hierarchies, and the warnings anchored
/// on the union component.
/// </summary>
public class PolymorphismAlternativesTests(PolymorphismAlternativesFixture fixture) : IClassFixture<PolymorphismAlternativesFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";

    public static TheoryData<OpenApiSpecVersion> Versions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Schemas(OpenApiSpecVersion version) =>
        fixture.Documents[version]["components"]!["schemas"]!.AsObject();

    private string Component(OpenApiSpecVersion version, string path) =>
        Id(fixture.Documents[version]["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!
            ["application/json"]!["schema"]!["$ref"]!.GetValue<string>());

    private static string Id(string reference)
    {
        reference.Should().StartWith(SchemasPrefix);
        return reference[SchemasPrefix.Length..];
    }

    private static List<string> Alternatives(JsonNode union, string keyword) =>
        union[keyword]!.AsArray().Select(a => Id(a!["$ref"]!.GetValue<string>())).ToList();

    private static IReadOnlyList<string> PropertyNames(JsonNode schema) =>
        schema["properties"]?.AsObject().Select(p => p.Key).ToList() ?? [];

    private static IReadOnlyList<string> Required(JsonNode schema) =>
        schema["required"]?.AsArray().Select(r => r!.GetValue<string>()).ToList() ?? [];

    // ── Concrete base: variants plus the base branch ─────────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void ConcreteBase_OneOfVariantsAndBaseBranch_NoDiscriminatorObjectYet(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);
        var union = schemas[Component(version, "/polymorphism-alt/pet")]!;

        union.AsObject().ContainsKey("discriminator").Should().BeFalse();
        var alternatives = Alternatives(union, "oneOf");
        alternatives.Should().HaveCount(3);

        var withValue = alternatives.Where(a => PropertyNames(schemas[a]!).Contains("$type")).ToList();
        withValue.Should().HaveCount(2, because: "the base is declared with a value, so it has a variant of its own");
        withValue.Should().AllSatisfy(a => Required(schemas[a]!).Should().Contain("$type"));

        var branch = schemas[alternatives.Single(a => !withValue.Contains(a))]!;
        PropertyNames(branch).Should().Equal("nickname");
        Required(branch).Should().NotContain("$type");
    }

    [Fact]
    public void BaseBranch_RejectsAnyDiscriminator_WhenUnknownValuesAreRejected()
    {
        var document = fixture.Documents[OpenApiSpecVersion.OpenApi3_1];
        var conformance = SchemaConformance.For(document);
        var branch = BranchOf(document, "/polymorphism-alt/pet");

        conformance.ValidateComponent(branch, JsonNode.Parse("""{"nickname":"Rex"}""")).IsValid.Should().BeTrue();
        conformance.ValidateComponent(branch, JsonNode.Parse("""{"$type":"hamster","nickname":"Rex"}""")).IsValid.Should().BeFalse();
        conformance.ValidateComponent(branch, JsonNode.Parse("""{"$type":"unknown","nickname":"Rex"}""")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void BaseBranch_RejectsOnlyMappedValues_WhenUnknownValuesAreRead()
    {
        var document = fixture.Documents[OpenApiSpecVersion.OpenApi3_1];
        var conformance = SchemaConformance.For(document);
        var branch = BranchOf(document, "/polymorphism-alt/fish");

        conformance.ValidateComponent(branch, JsonNode.Parse("""{"fins":2}""")).IsValid.Should().BeTrue();
        conformance.ValidateComponent(branch, JsonNode.Parse("""{"$type":"whale","fins":2}""")).IsValid.Should().BeTrue();
        conformance.ValidateComponent(branch, JsonNode.Parse("""{"$type":"shark","fins":2}""")).IsValid.Should().BeFalse();
    }

    private string BranchOf(JsonNode document, string path)
    {
        var schemas = document["components"]!["schemas"]!.AsObject();
        var union = schemas[Component(OpenApiSpecVersion.OpenApi3_1, path)]!;
        // Variants require the discriminator; the base branch never does.
        return Alternatives(union, "oneOf").Single(a => !Required(schemas[a]!).Contains("$type"));
    }

    // ── Derived types without a value: anyOf ─────────────────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void DerivedWithoutValue_AnyOfWithoutDiscriminator(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);
        var union = schemas[Component(version, "/polymorphism-excluded/message")]!;

        union.AsObject().ContainsKey("discriminator").Should().BeFalse();
        union.AsObject().ContainsKey("oneOf").Should().BeFalse();
        var alternatives = Alternatives(union, "anyOf").Select(a => schemas[a]!).ToList();
        alternatives.Should().HaveCount(3, because: "text variant, image without a value, and the concrete base");

        alternatives.Where(a => PropertyNames(a).Contains("text")).Should().ContainSingle()
            .Which.Should().Match<JsonNode>(a => Required(a).Contains("$type"));
        var image = alternatives.Single(a => PropertyNames(a).Contains("url"));
        PropertyNames(image).Should().NotContain("$type");
        Required(image).Should().NotContain("$type");
        var baseBranch = alternatives.Single(a => PropertyNames(a).SequenceEqual(["sender"]));
        baseBranch.AsObject().ContainsKey("not").Should().BeFalse(because: "in anyOf the base branch has no constraint");

        var gadget = schemas[Component(version, "/polymorphism-alt/gadget")]!;
        gadget.AsObject().ContainsKey("discriminator").Should().BeFalse();
        Alternatives(gadget, "anyOf").Should().HaveCount(2, because: "an abstract base has no base branch");
    }

    // ── Direct use, nesting, recursion, name collisions ──────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void DirectUse_HasNoDiscriminator(OpenApiSpecVersion version)
    {
        var hamster = Schemas(version)[Component(version, "/polymorphism-alt/hamster")]!;

        PropertyNames(hamster).Should().NotContain("$type");
        Required(hamster).Should().NotContain("$type");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void Configuration_IsNotInherited(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);

        var creature = schemas[Component(version, "/polymorphism-alt/creature")]!;
        var creatureAlternatives = Alternatives(creature, "oneOf");
        creatureAlternatives.Should().ContainSingle(because: "Creature declares only Bird, not Bird's own derived types");
        PropertyNames(schemas[creatureAlternatives[0]]!).Should().Contain(["$type", "wings", "habitat"]);

        var bird = schemas[Component(version, "/polymorphism-alt/bird")]!;
        bird["discriminator"]!["mapping"]!.AsObject().Select(m => m.Key).Should().Equal("parrot");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void RecursiveHierarchy_IsFinite_ReferencesResolve(OpenApiSpecVersion version)
    {
        var document = fixture.Documents[version];
        var schemas = Schemas(version);
        var tree = schemas[Component(version, "/polymorphism-alt/tree")]!;
        var branch = schemas[Id(tree["discriminator"]!["mapping"]!["branch"]!.GetValue<string>())]!;

        branch["properties"]!["children"]!["items"]!["$ref"]!.GetValue<string>()
            .Should().Be(SchemasPrefix + Component(version, "/polymorphism-alt/tree"));
        JsonReferences.All(document).Where(r => JsonReferences.Resolve(document, r) == null).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void DtoNamedLikeTheBaseBranch_IsADifferentComponent(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);
        var dto = Component(version, "/polymorphism-alt/pet-default");
        var branch = Alternatives(schemas[Component(version, "/polymorphism-alt/pet")]!, "oneOf")
            .Single(a => !PropertyNames(schemas[a]!).Contains("$type"));

        dto.Should().NotBe(branch);
        PropertyNames(schemas[dto]!).Should().Equal("owner");
        PropertyNames(schemas[branch]!).Should().Equal("nickname");
    }

    // ── Warnings anchored on the union ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void Warnings_OncePerReachableUnion(OpenApiSpecVersion version)
    {
        var diagnostics = fixture.Diagnostics[version];
        var message = SchemasPrefix + Component(version, "/polymorphism-excluded/message");
        var ticket = SchemasPrefix + Component(version, "/polymorphism-excluded/ticket");

        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.PolymorphismAnyOfWithoutDiscriminator && d.Location == message)
            .Should().ContainSingle()
            .Which.Subjects.Should().Equal(typeof(ImageMessage).FullName);
        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.PolymorphismSourceDisagreement && d.Location == ticket)
            .Should().ContainSingle()
            .Which.Subjects.Should().Equal(typeof(Ticket).FullName);
    }

    [Fact]
    public void Warnings_NoneForUnionsReachableOnlyFromExcludedOperations()
    {
        var message = SchemasPrefix + Component(OpenApiSpecVersion.OpenApi3_0, "/polymorphism-excluded/message");
        var ticket = SchemasPrefix + Component(OpenApiSpecVersion.OpenApi3_0, "/polymorphism-excluded/ticket");

        fixture.Excluded30.Should().NotContain(d => d.Location == message || d.Location == ticket);
        fixture.Excluded30.Should().Contain(d => d.Code == ExtractionDiagnosticCodes.PolymorphismAnyOfWithoutDiscriminator,
            because: "unions reachable from kept operations (Gadget) are still reported");
    }

    [Theory]
    [InlineData(typeof(Message), ExtractionDiagnosticCodes.PolymorphismAnyOfWithoutDiscriminator)]
    [InlineData(typeof(Ticket), ExtractionDiagnosticCodes.PolymorphismSourceDisagreement)]
    public void SchemaGenerator_WarnsOncePerInstance(Type type, string code)
    {
        var first = new List<ExtractionDiagnostic>();
        var generator = new SchemaGenerator(new SchemaOptions { OnDiagnostic = first.Add });
        generator.GenerateSchema(type);
        generator.GenerateSchema(type);

        var second = new List<ExtractionDiagnostic>();
        new SchemaGenerator(new SchemaOptions { OnDiagnostic = second.Add }).GenerateSchema(type);

        first.Where(d => d.Code == code).Should().ContainSingle()
            .Which.Location.Should().Be(SchemasPrefix + type.Name);
        second.Where(d => d.Code == code).Should().ContainSingle();
    }
}
