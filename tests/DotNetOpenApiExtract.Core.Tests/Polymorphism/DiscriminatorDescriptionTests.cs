using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using ModernApi.Models.Polymorphism;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Polymorphism;

/// <summary>
/// The discriminator property of a variant and of a base branch has no CLR member to document it:
/// the generator describes it, so <c>schema.property-description</c> asks only for the members of the code.
/// </summary>
public sealed class DiscriminatorDescriptionTests
{
    public static TheoryData<OpenApiSpecVersion> Versions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(Versions))]
    public void PropertyDescriptionRule_ReportsMembersOfTheCode_NotTheGeneratedDiscriminators(OpenApiSpecVersion version)
    {
        // No XML documentation: every member of the code is undescribed.
        var generator = new SchemaGenerator(new SchemaOptions { OpenApiVersion = version });
        generator.GenerateSchema(typeof(Animal));
        generator.GenerateSchema(typeof(IShape));
        generator.GenerateSchema(typeof(Fish));
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                Schemas = generator.Schemas.ToDictionary(kv => kv.Key, kv => (IOpenApiSchema)kv.Value),
            },
        };
        string[] discriminators = ["$type", "kind"];
        var generated = generator.Schemas.Values
            .SelectMany(s => s.Properties ?? new Dictionary<string, IOpenApiSchema>())
            .Where(p => discriminators.Contains(p.Key))
            .ToList();
        // Animal, Fish: $type in each variant and in Fish's base branch; IShape: kind in each variant.
        generated.Should().HaveCount(6);

        var reported = new SchemaPropertyDescriptionRule().Validate(document, new ValidationContext())
            .Select(v => v.JsonPointer.Split('/')[^1])
            .ToList();

        generated.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(((OpenApiSchema)p.Value).Description));
        reported.Should().NotContain(discriminators);
        reported.Should().Contain(["name", "lives", "breed", "fins", "teeth"]);
    }
}
