using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// <c>discriminator.default-mapping-when-optional</c> (OpenAPI 3.2): a discriminator whose property an
/// instance may lack needs a <c>defaultMapping</c>; required-ness is evaluated through
/// <c>allOf</c> / <c>oneOf</c> / <c>anyOf</c> and <c>$ref</c>.
/// </summary>
public sealed class DiscriminatorDefaultMappingRuleTests
{
    private const string RuleId = "discriminator.default-mapping-when-optional";
    private const string Property = "kind";

    private static IReadOnlyList<ValidationViolation> Violations(OpenApiDocument document, OpenApiSpecVersion version)
    {
        // Built with the defaults: the rule is on without --enable-rule.
        var result = CoreValidator.Validate(document, new ValidationContext { OpenApiSpecVersion = version });
        result.SkippedRules.Should().NotContain(RuleId);
        return result.Violations.Where(v => v.RuleId == RuleId).ToList();
    }

    private static OpenApiSchema Object(bool requiresKind) => new()
    {
        Type       = JsonSchemaType.Object,
        Properties = new Dictionary<string, IOpenApiSchema> { [Property] = new OpenApiSchema { Type = JsonSchemaType.String } },
        Required   = requiresKind ? new HashSet<string> { Property } : null,
    };

    /// <summary>
    /// A 3.2 document whose component <c>Pet</c> holds the discriminator; <paramref name="configure"/>
    /// shapes <c>Pet</c> and may add components (<c>Cat</c> and <c>Dog</c> are there to reference).
    /// </summary>
    private static OpenApiDocument Document(Action<OpenApiDocument, OpenApiSchema> configure, bool defaultMapping = false)
    {
        var document = new OpenApiDocument
        {
            Info       = new OpenApiInfo { Title = "T", Version = "1" },
            Paths      = new OpenApiPaths(),
            Components = new OpenApiComponents { Schemas = new Dictionary<string, IOpenApiSchema>() },
        };
        var pet = new OpenApiSchema
        {
            Discriminator = new OpenApiDiscriminator
            {
                PropertyName = Property,
                Mapping = new Dictionary<string, OpenApiSchemaReference>
                {
                    ["cat"] = new OpenApiSchemaReference("Cat", document),
                    ["dog"] = new OpenApiSchemaReference("Dog", document),
                },
                DefaultMapping = defaultMapping ? new OpenApiSchemaReference("PetDefault", document) : null,
            },
        };
        document.Components.Schemas["Pet"] = pet;
        document.Components.Schemas["PetDefault"] = Object(requiresKind: false);
        configure(document, pet);
        return document;
    }

    private static void Alternatives(OpenApiDocument document, OpenApiSchema pet, bool oneOf, bool catRequires, bool dogRequires)
    {
        document.Components!.Schemas!["Cat"] = Object(catRequires);
        document.Components.Schemas["Dog"] = Object(dogRequires);
        IList<IOpenApiSchema> alternatives = [new OpenApiSchemaReference("Cat", document), new OpenApiSchemaReference("Dog", document)];
        if (oneOf)
            pet.OneOf = alternatives;
        else
            pet.AnyOf = alternatives;
    }

    public static TheoryData<string, Action<OpenApiDocument, OpenApiSchema>> RequiredShapes => new()
    {
        { "own required", (d, pet) => { Alternatives(d, pet, oneOf: true, false, false); pet.Required = new HashSet<string> { Property }; } },
        { "allOf element", (d, pet) => { Alternatives(d, pet, oneOf: true, false, false); pet.AllOf = [new OpenApiSchema(), Object(requiresKind: true)]; } },
        { "allOf $ref", (d, pet) =>
            {
                Alternatives(d, pet, oneOf: true, false, false);
                d.Components!.Schemas!["KindHolder"] = Object(requiresKind: true);
                pet.AllOf = [new OpenApiSchemaReference("KindHolder", d)];
            } },
        { "every oneOf branch", (d, pet) => Alternatives(d, pet, oneOf: true, catRequires: true, dogRequires: true) },
        { "every anyOf branch", (d, pet) => Alternatives(d, pet, oneOf: false, catRequires: true, dogRequires: true) },
        { "branch through a nested allOf $ref", (d, pet) =>
            {
                Alternatives(d, pet, oneOf: true, catRequires: true, dogRequires: false);
                d.Components!.Schemas!["KindHolder"] = Object(requiresKind: true);
                d.Components.Schemas["Dog"] = new OpenApiSchema { AllOf = [new OpenApiSchemaReference("KindHolder", d)] };
            } },
    };

    [Theory]
    [MemberData(nameof(RequiredShapes))]
    public void RequiredThroughCompositionOrReference_NoViolation(string shape, Action<OpenApiDocument, OpenApiSchema> configure)
    {
        _ = shape;
        Violations(Document(configure), OpenApiSpecVersion.OpenApi3_2).Should().BeEmpty();
    }

    public static TheoryData<string, Action<OpenApiDocument, OpenApiSchema>> OptionalShapes => new()
    {
        { "nowhere", (d, pet) => Alternatives(d, pet, oneOf: true, false, false) },
        // The first branch requires it, the second does not: required-ness is not the first hit.
        { "mixed oneOf", (d, pet) => Alternatives(d, pet, oneOf: true, catRequires: true, dogRequires: false) },
        { "mixed anyOf", (d, pet) => Alternatives(d, pet, oneOf: false, catRequires: true, dogRequires: false) },
        { "allOf without it", (d, pet) => { Alternatives(d, pet, oneOf: true, false, false); pet.AllOf = [Object(requiresKind: false)]; } },
    };

    [Theory]
    [MemberData(nameof(OptionalShapes))]
    public void Optional_WithoutDefaultMapping_Violates(string shape, Action<OpenApiDocument, OpenApiSchema> configure)
    {
        _ = shape;
        var violation = Violations(Document(configure), OpenApiSpecVersion.OpenApi3_2).Should().ContainSingle().Subject;
        violation.JsonPointer.Should().Be("#/components/schemas/Pet/discriminator");
        violation.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(OptionalShapes))]
    public void Optional_WithDefaultMapping_NoViolation(string shape, Action<OpenApiDocument, OpenApiSchema> configure)
    {
        _ = shape;
        Violations(Document(configure, defaultMapping: true), OpenApiSpecVersion.OpenApi3_2).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    public void BeforeV32_NotApplied(OpenApiSpecVersion version)
    {
        Violations(Document((d, pet) => Alternatives(d, pet, oneOf: true, false, false)), version).Should().BeEmpty();
    }

    [Fact]
    public void InlineDiscriminatorInAResponse_IsChecked()
    {
        var document = Document((d, pet) => Alternatives(d, pet, oneOf: true, true, true));
        var inline = new OpenApiSchema
        {
            OneOf = [new OpenApiSchemaReference("Cat", document), new OpenApiSchemaReference("PetDefault", document)],
            Discriminator = new OpenApiDiscriminator { PropertyName = Property },
        };
        document.Paths["/pets"] = new OpenApiPathItem
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
                            Content = new Dictionary<string, IOpenApiMediaType> { ["application/json"] = new OpenApiMediaType { Schema = inline } },
                        },
                    },
                },
            },
        };

        Violations(document, OpenApiSpecVersion.OpenApi3_2).Should().ContainSingle()
            .Which.JsonPointer.Should().Be("#/paths/~1pets/get/responses/200/content/application~1json/schema/discriminator");
    }
}

/// <summary>
/// Every place of the document where a schema can stand is walked: an optional discriminator is
/// reported at its exact pointer wherever it is.
/// </summary>
public sealed class DiscriminatorDefaultMappingPlacesTests
{
    private const string RuleId = "discriminator.default-mapping-when-optional";

    /// <summary>A union whose discriminator property no alternative requires, with no defaultMapping.</summary>
    private static OpenApiSchema OptionalUnion() => new()
    {
        OneOf =
        [
            new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["kind"] = new OpenApiSchema { Type = JsonSchemaType.String } } },
            new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["kind"] = new OpenApiSchema { Type = JsonSchemaType.String } } },
        ],
        Discriminator = new OpenApiDiscriminator { PropertyName = "kind" },
    };

    private static OpenApiOperation Operation(Action<OpenApiOperation>? configure = null)
    {
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses { ["204"] = new OpenApiResponse { Description = "Done" } },
        };
        configure?.Invoke(operation);
        return operation;
    }

    private static OpenApiPathItem PathItem(OpenApiOperation operation, IList<IOpenApiParameter>? parameters = null) => new()
    {
        Parameters = parameters,
        Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Post] = operation },
    };

    private static OpenApiParameter QueryParameter(OpenApiSchema schema) => new() { Name = "filter", In = ParameterLocation.Query, Schema = schema };

    private static Dictionary<string, IOpenApiMediaType> Json(OpenApiSchema schema) =>
        new() { ["application/json"] = new OpenApiMediaType { Schema = schema } };

    public static TheoryData<string, Action<OpenApiDocument>, string> Places => new()
    {
        { "contentSchema", d => d.Components!.Schemas!["Data"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String, ContentMediaType = "application/json", ContentSchema = OptionalUnion(),
            }, "#/components/schemas/Data/contentSchema/discriminator" },
        { "not", d => d.Components!.Schemas!["Data"] = new OpenApiSchema { Not = OptionalUnion() }, "#/components/schemas/Data/not/discriminator" },
        { "propertyNames", d => d.Components!.Schemas!["Data"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object, AdditionalProperties = new OpenApiSchema(), PropertyNames = OptionalUnion(),
            }, "#/components/schemas/Data/propertyNames/discriminator" },
        { "patternProperties", d => d.Components!.Schemas!["Data"] = new OpenApiSchema
            {
                PatternProperties = new Dictionary<string, IOpenApiSchema> { ["^a/b$"] = OptionalUnion() },
            }, "#/components/schemas/Data/patternProperties/^a~1b$/discriminator" },
        { "if", d => d.Components!.Schemas!["Data"] = new OpenApiSchema { If = OptionalUnion() }, "#/components/schemas/Data/if/discriminator" },
        { "path-item parameter", d => d.Paths["/pets/{id}"] = PathItem(Operation(), [QueryParameter(OptionalUnion())]),
            "#/paths/~1pets~1{id}/parameters/0/schema/discriminator" },
        { "webhook path-item parameter", d => d.Webhooks = new Dictionary<string, IOpenApiPathItem>
            {
                ["created"] = PathItem(Operation(), [QueryParameter(OptionalUnion())]),
            }, "#/webhooks/created/parameters/0/schema/discriminator" },
        { "parameter content", d => d.Paths["/pets"] = PathItem(Operation(o => o.Parameters =
            [
                new OpenApiParameter { Name = "q", In = ParameterLocation.Query, Content = Json(OptionalUnion()) },
            ])), "#/paths/~1pets/post/parameters/0/content/application~1json/schema/discriminator" },
        { "response header", d => d.Paths["/pets"] = PathItem(Operation(o => o.Responses!["204"] = new OpenApiResponse
            {
                Description = "Done",
                Headers = new Dictionary<string, IOpenApiHeader> { ["X-Kind"] = new OpenApiHeader { Schema = OptionalUnion() } },
            })), "#/paths/~1pets/post/responses/204/headers/X-Kind/schema/discriminator" },
        { "component response", d => d.Components!.Responses = new Dictionary<string, IOpenApiResponse>
            {
                ["Pet"] = new OpenApiResponse { Description = "Pet", Content = Json(OptionalUnion()) },
            }, "#/components/responses/Pet/content/application~1json/schema/discriminator" },
        { "component parameter", d => d.Components!.Parameters = new Dictionary<string, IOpenApiParameter>
            {
                ["Filter"] = QueryParameter(OptionalUnion()),
            }, "#/components/parameters/Filter/schema/discriminator" },
        { "component request body", d => d.Components!.RequestBodies = new Dictionary<string, IOpenApiRequestBody>
            {
                ["Pet"] = new OpenApiRequestBody { Content = Json(OptionalUnion()) },
            }, "#/components/requestBodies/Pet/content/application~1json/schema/discriminator" },
        { "component header", d => d.Components!.Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["X-Kind"] = new OpenApiHeader { Schema = OptionalUnion() },
            }, "#/components/headers/X-Kind/schema/discriminator" },
    };

    [Theory]
    [MemberData(nameof(Places))]
    public void OptionalDiscriminator_IsReportedWhereverItStands(string place, Action<OpenApiDocument> configure, string pointer)
    {
        _ = place;
        var document = new OpenApiDocument
        {
            Info       = new OpenApiInfo { Title = "T", Version = "1" },
            Paths      = new OpenApiPaths(),
            Components = new OpenApiComponents { Schemas = new Dictionary<string, IOpenApiSchema>() },
        };
        configure(document);

        var result = CoreValidator.Validate(document, new ValidationContext { OpenApiSpecVersion = OpenApiSpecVersion.OpenApi3_2 });

        result.SkippedRules.Should().NotContain(RuleId);
        result.Violations.Where(v => v.RuleId == RuleId).Select(v => v.JsonPointer).Should().Equal(pointer);
    }
}

/// <summary>Builds ModernApi for OpenAPI 3.2 with validation once.</summary>
public sealed class ModernApiV32ValidationFixture
{
    public ModernApiV32ValidationFixture()
    {
        Document = OpenApiDocumentBuilder.BuildWithValidation(
            VersionedDocumentHarness.ModernApiOptions(OpenApiSpecVersion.OpenApi3_2), new ValidationContext(), out var result);
        Result = result;
    }

    public OpenApiDocument Document { get; }

    public ValidationResult Result { get; }
}

/// <summary>The generator's 3.2 polymorphism output always passes the rule: a concrete base gets a <c>defaultMapping</c>.</summary>
public sealed class DiscriminatorDefaultMappingOnGeneratedOutputTests(ModernApiV32ValidationFixture fixture)
    : IClassFixture<ModernApiV32ValidationFixture>
{
    [Fact]
    public void ModernApi_V32_NoViolation()
    {
        var discriminators = fixture.Document.Components!.Schemas!.Values
            .OfType<OpenApiSchema>().Select(s => s.Discriminator).OfType<OpenApiDiscriminator>().ToList();
        discriminators.Should().NotBeEmpty();
        discriminators.Should().Contain(d => d.DefaultMapping != null, because: "the fixture has a concrete polymorphic base");

        fixture.Result.SkippedRules.Should().NotContain("discriminator.default-mapping-when-optional");
        fixture.Result.Violations.Should().NotContain(v => v.RuleId == "discriminator.default-mapping-when-optional");
    }
}
