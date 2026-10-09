using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Polymorphism;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Polymorphism;

/// <summary>
/// The <c>discriminator</c> object by version: always for an abstract base or interface; for a
/// concrete base only in 3.2, with <c>defaultMapping</c> on the base branch, and a warning instead
/// in 3.0/3.1; never for an <c>anyOf</c> union. The last test checks the whole US-10 table at once.
/// </summary>
public class DiscriminatorVersionTests(PolymorphismAlternativesFixture fixture) : IClassFixture<PolymorphismAlternativesFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";
    private const string NotExpressible = ExtractionDiagnosticCodes.PolymorphismDiscriminatorNotExpressible;

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

    /// <summary>The base branch: the oneOf alternative that does not require the discriminator.</summary>
    private string BaseBranch(OpenApiSpecVersion version, JsonNode union)
    {
        var schemas = Schemas(version);
        return union["oneOf"]!.AsArray()
            .Select(a => Id(a!["$ref"]!.GetValue<string>()))
            .Single(a => !(schemas[a]!["required"]?.AsArray().Any(r => r!.GetValue<string>() == "$type") ?? false));
    }

    [Theory]
    [InlineData("/polymorphism-alt/pet")]
    [InlineData("/polymorphism-alt/fish")]
    [InlineData("/polymorphism-alt/fruit")]
    public void ConcreteBase_Target32_DefaultMappingNamesTheBaseBranch(string path)
    {
        const OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_2;
        var unionId = Component(version, path);
        var union = Schemas(version)[unionId]!;
        var discriminator = union["discriminator"]!;

        discriminator["propertyName"]!.GetValue<string>().Should().Be("$type");
        var defaultMapping = discriminator["defaultMapping"]!.GetValue<string>();
        defaultMapping.Should().Be(SchemasPrefix + BaseBranch(version, union));
        defaultMapping.Should().NotBe(SchemasPrefix + unionId);
        discriminator["mapping"]!.AsObject().Select(m => m.Value!.GetValue<string>())
            .Should().NotContain(defaultMapping, because: "the base's own variant (with its value) is not the base branch");
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    public void ConcreteBase_Downlevel_NoDiscriminatorObject_OneWarningPerUnion(OpenApiSpecVersion version)
    {
        foreach (var path in new[] { "/polymorphism-alt/pet", "/polymorphism-alt/fish", "/polymorphism-alt/fruit" })
        {
            var unionId = Component(version, path);
            Schemas(version)[unionId]!.AsObject().ContainsKey("discriminator").Should().BeFalse();

            var warning = fixture.Diagnostics[version]
                .Where(d => d.Code == NotExpressible && d.Location == SchemasPrefix + unionId)
                .Should().ContainSingle().Subject;
            warning.Action.Should().Be(DiagnosticAction.Omitted);
            warning.RequiredVersion.Should().Be(OpenApiSpecVersion.OpenApi3_2);
            warning.TargetVersion.Should().Be(version);
        }
    }

    [Fact]
    public void Target32_NoDiscriminatorWarning()
    {
        fixture.Diagnostics[OpenApiSpecVersion.OpenApi3_2].Should().NotContain(d => d.Code == NotExpressible);
    }

    [Fact]
    public void UnionOnlyOnExcludedPath_NoDiscriminatorWarning()
    {
        var coupon = SchemasPrefix + Component(OpenApiSpecVersion.OpenApi3_0, "/polymorphism-excluded/coupon");

        fixture.Diagnostics[OpenApiSpecVersion.OpenApi3_0]
            .Should().Contain(d => d.Code == NotExpressible && d.Location == coupon,
                because: "without the exclusion the union is reported");
        fixture.Excluded30.Should().NotContain(d => d.Location == coupon);
    }

    [Fact]
    public void SchemaGenerator_DiscriminatorWarningOncePerInstance()
    {
        var first = new List<ExtractionDiagnostic>();
        var generator = new SchemaGenerator(new SchemaOptions { OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, OnDiagnostic = first.Add });
        generator.GenerateSchema(typeof(Pet));
        generator.GenerateSchema(typeof(Pet));

        var second = new List<ExtractionDiagnostic>();
        new SchemaGenerator(new SchemaOptions { OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, OnDiagnostic = second.Add })
            .GenerateSchema(typeof(Pet));

        first.Where(d => d.Code == NotExpressible).Should().ContainSingle();
        second.Where(d => d.Code == NotExpressible).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void UsTenTable_AllRowsTogether(OpenApiSpecVersion version)
    {
        var schemas = Schemas(version);

        // Row 1: abstract base, every derived type with a value.
        var tree = schemas[Component(version, "/polymorphism-alt/tree")]!.AsObject();
        tree.ContainsKey("oneOf").Should().BeTrue();
        tree.ContainsKey("anyOf").Should().BeFalse();
        tree["discriminator"]!.AsObject().ContainsKey("defaultMapping").Should().BeFalse();
        tree["oneOf"]!.AsArray().Should().HaveCount(2);

        // Row 2: a derived type without a value.
        var message = schemas[Component(version, "/polymorphism-excluded/message")]!.AsObject();
        message.ContainsKey("anyOf").Should().BeTrue();
        message.ContainsKey("oneOf").Should().BeFalse();
        message.ContainsKey("discriminator").Should().BeFalse();
        message["anyOf"]!.AsArray().Should().HaveCount(3);

        // Row 3: concrete base declared with a value.
        var pet = schemas[Component(version, "/polymorphism-alt/pet")]!.AsObject();
        pet.ContainsKey("oneOf").Should().BeTrue();
        pet["oneOf"]!.AsArray().Should().HaveCount(3);
        pet.ContainsKey("discriminator").Should().Be(version == OpenApiSpecVersion.OpenApi3_2);
        if (version == OpenApiSpecVersion.OpenApi3_2)
            pet["discriminator"]!.AsObject().ContainsKey("defaultMapping").Should().BeTrue();
    }
}
