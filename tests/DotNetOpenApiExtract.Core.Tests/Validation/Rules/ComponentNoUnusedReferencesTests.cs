using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// <c>component.no-unused</c>: a component referenced only through <c>itemSchema</c>,
/// <c>contentSchema</c>, <c>propertyNames</c>, <c>oneOf</c> / <c>anyOf</c> or <c>discriminator.mapping</c>
/// is used; a component nothing references is reported.
/// </summary>
public sealed class ComponentNoUnusedReferencesTests
{
    private const string RuleId = "component.no-unused";

    private static readonly ValidationContext Enabled = new()
    {
        OpenApiSpecVersion = OpenApiSpecVersion.OpenApi3_2,
        EnabledRuleIds     = new HashSet<string> { RuleId },
    };

    /// <summary>
    /// A document with the components <c>Target</c> (the one under test) and <c>Unused</c>; a
    /// <c>GET /items</c> operation whose 200 response has the media type <paramref name="mediaType"/>.
    /// </summary>
    private static OpenApiDocument Document(Func<OpenApiDocument, OpenApiMediaType> mediaType, Action<OpenApiDocument>? configure = null)
    {
        var document = new OpenApiDocument
        {
            Info       = new OpenApiInfo { Title = "T", Version = "1" },
            Paths      = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Target"] = new OpenApiSchema { Type = JsonSchemaType.Object },
                    ["Unused"] = new OpenApiSchema { Type = JsonSchemaType.Object },
                },
            },
        };
        document.Paths["/items"] = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = new()
                {
                    Responses = new OpenApiResponses
                    {
                        ["200"] = new OpenApiResponse
                        {
                            Description = "OK",
                            Content = new Dictionary<string, IOpenApiMediaType> { ["application/x-ndjson"] = mediaType(document) },
                        },
                    },
                },
            },
        };
        configure?.Invoke(document);
        return document;
    }

    private static OpenApiSchemaReference Ref(OpenApiDocument document, string id) => new(id, document);

    /// <summary>A component <c>Holder</c>, referenced by the response, holding <paramref name="schema"/>'s reference.</summary>
    private static Func<OpenApiDocument, OpenApiMediaType> ThroughHolder(Func<OpenApiDocument, OpenApiSchema> holder) => document =>
    {
        document.Components!.Schemas!["Holder"] = holder(document);
        return new OpenApiMediaType { Schema = Ref(document, "Holder") };
    };

    public static TheoryData<string, Func<OpenApiDocument, OpenApiMediaType>> References => new()
    {
        { "itemSchema", d => new OpenApiMediaType { ItemSchema = Ref(d, "Target") } },
        { "contentSchema", d => new OpenApiMediaType
            {
                ItemSchema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["data"] = new OpenApiSchema { Type = JsonSchemaType.String, ContentMediaType = "application/json", ContentSchema = Ref(d, "Target") },
                    },
                },
            } },
        { "propertyNames", ThroughHolder(d => new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Integer },
                PropertyNames = Ref(d, "Target"),
            }) },
        { "oneOf", ThroughHolder(d => new OpenApiSchema { OneOf = [Ref(d, "Target")] }) },
        { "anyOf", ThroughHolder(d => new OpenApiSchema { AnyOf = [Ref(d, "Target"), new OpenApiSchema { Type = JsonSchemaType.Null }] }) },
        { "discriminator.mapping", ThroughHolder(d => new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Discriminator = new OpenApiDiscriminator
                {
                    PropertyName = "kind",
                    Mapping = new Dictionary<string, OpenApiSchemaReference> { ["target"] = Ref(d, "Target") },
                },
            }) },
    };

    [Theory]
    [MemberData(nameof(References))]
    public void ReferencedOnlyThrough_IsUsed(string through, Func<OpenApiDocument, OpenApiMediaType> mediaType)
    {
        _ = through;
        var result = CoreValidator.Validate(Document(mediaType), Enabled);

        result.SkippedRules.Should().NotContain(RuleId);
        // The control: a component nothing references is still reported, and only it.
        result.Violations.Where(v => v.RuleId == RuleId).Select(v => v.JsonPointer)
            .Should().Equal("#/components/schemas/Unused");
    }
}

/// <summary>Builds ModernApi with validation, <c>component.no-unused</c> enabled, once per OpenAPI version.</summary>
public sealed class ModernApiNoUnusedFixture
{
    public ModernApiNoUnusedFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = OpenApiDocumentBuilder.BuildWithValidation(
                VersionedDocumentHarness.ModernApiOptions(version),
                new ValidationContext { EnabledRuleIds = new HashSet<string> { "component.no-unused" } },
                out var result);
            Documents[version] = document;
            Results[version] = result;
        }
    }

    public Dictionary<OpenApiSpecVersion, OpenApiDocument> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, ValidationResult> Results { get; } = [];
}

/// <summary>
/// The generator's own output in every version passes <c>schema.property-constraints</c> and
/// <c>component.no-unused</c>: streaming item schemas, event data, dictionary keys, unions and
/// discriminator mappings are references, and every attribute constraint is in the schema.
/// </summary>
public sealed class ConstraintAndUsageRulesOnGeneratedOutputTests(ModernApiNoUnusedFixture fixture)
    : IClassFixture<ModernApiNoUnusedFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ModernApi_NoFalsePositives(OpenApiSpecVersion version)
    {
        var result = fixture.Results[version];

        // The input has what the rules must accept: exclusive bounds, bounds on a number-handling
        // branch, and components reached only through an item schema.
        var properties = fixture.Documents[version].Components!.Schemas!.Values.OfType<OpenApiSchema>()
            .SelectMany(s => s.Properties?.Values ?? []).OfType<OpenApiSchema>().ToList();
        properties.Should().Contain(p => p.ExclusiveMinimum != null);
        properties.Select(p => p.AnyOf?.FirstOrDefault() as OpenApiSchema)
            .Should().Contain(branch => branch != null && (branch.Minimum != null || branch.Maximum != null));

        result.SkippedRules.Should().NotContain(["schema.property-constraints", "component.no-unused"]);
        result.Violations.Where(v => v.RuleId is "schema.property-constraints" or "component.no-unused")
            .Should().BeEmpty();
    }
}
