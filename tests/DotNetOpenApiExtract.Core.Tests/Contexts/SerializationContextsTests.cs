using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using DotNetOpenApiExtract.Core.Tests.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using ModernApi.Models.Contexts;
using Xunit;
using StjNamingPolicy = System.Text.Json.JsonNamingPolicy;

namespace DotNetOpenApiExtract.Core.Tests.Contexts;

/// <summary>Builds ModernApi with a temporary Program.cs.</summary>
internal static class ContextBuilds
{
    public static string ProgramCs(string? mvcOptions = null, string? httpOptions = null)
    {
        var mvc = mvcOptions == null
            ? "builder.Services.AddControllers();"
            : $"builder.Services.AddControllers().AddJsonOptions(o => {{ {mvcOptions} }});";
        var http = httpOptions == null ? string.Empty : $"builder.Services.ConfigureHttpJsonOptions(o => {{ {httpOptions} }});";
        return $"""
            var builder = WebApplication.CreateBuilder(args);
            {mvc}
            {http}
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """;
    }

    public static CollectedBuild Build(
        string programCs,
        OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_2,
        Core.JsonNamingPolicy? cliPolicy = null,
        IReadOnlyList<string>? excludePathPrefixes = null)
    {
        using var source = new TempDirectory();
        File.WriteAllText(Path.Combine(source.Path, "Program.cs"), programCs);

        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            SourceRoot          = source.Path,
            OpenApiVersion      = version,
            NamingPolicy        = cliPolicy,
            ExcludePathPrefixes = excludePathPrefixes,
            OnDiagnostic        = onDiagnostic,
        });
        var json = VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
        return new CollectedBuild(json, diagnostics);
    }
}

/// <summary>ModernApi with snake_case for the HTTP context only (MVC keeps the camelCase default), for 3.1 and 3.2.</summary>
public sealed class SplitContextsFixture
{
    public const string HttpSnakeCase = "o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;";

    public SplitContextsFixture()
    {
        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
        {
            Builds[version] = ContextBuilds.Build(ContextBuilds.ProgramCs(httpOptions: HttpSnakeCase), version);
            Conformance[version] = SchemaConformance.For(Builds[version].Document);
        }
    }

    public Dictionary<OpenApiSpecVersion, CollectedBuild> Builds { get; } = [];

    public Dictionary<OpenApiSpecVersion, SchemaConformance> Conformance { get; } = [];
}

/// <summary>
/// Controller bodies follow the MVC JSON options and server-sent events data the HTTP ones; when the
/// two differ in what shapes a schema, a type used in both contexts gets a schema per context
/// (<c>{Id}</c> and <c>{Id}Http</c>), every role of a polymorphic base included, and one warning names
/// those types.
/// </summary>
public class SerializationContextsTests(SplitContextsFixture fixture) : IClassFixture<SplitContextsFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";
    private const string SharedTypesCode = ExtractionDiagnosticCodes.SerializationContextsSharedTypes;

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    private static JsonNode Doc(CollectedBuild build) => build.Document;

    private static string Id(string reference)
    {
        reference.Should().StartWith(SchemasPrefix);
        return reference[SchemasPrefix.Length..];
    }

    private static string MvcBodyId(JsonNode document, string path) =>
        Id(document["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>());

    private static JsonNode ContentSchema(JsonNode document, string path)
    {
        var media = document["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!["text/event-stream"]!;
        var itemSchema = media["itemSchema"] ?? media["x-oai-itemSchema"]!; // 3.2 / 3.1
        return itemSchema["properties"]!["data"]!["contentSchema"]!;
    }

    private static IReadOnlyList<string> MappingIds(JsonObject union) =>
        union["discriminator"]!["mapping"]!.AsObject().Select(p => Id(p.Value!.GetValue<string>())).ToList();

    private static string SseDataId(JsonNode document, string path) =>
        Id(ContentSchema(document, path)["$ref"]!.GetValue<string>());

    private static JsonObject Component(JsonNode document, string id) =>
        document["components"]!["schemas"]![id]!.AsObject();

    private static IReadOnlyList<string> PropertyNames(JsonNode document, string id) =>
        Component(document, id)["properties"]!.AsObject().Select(p => p.Key).ToList();

    private static IReadOnlyList<string> RefsIn(JsonNode? node) =>
        JsonReferences.All(node).Select(Id).ToList();

    // ── Row: a plain type in both contexts ──────────────────────────────────

    [Fact]
    public void PlainType_MvcBodyAndEventData_ReferenceTheSchemaOfTheirContext()
    {
        var document = Doc(fixture.Builds[OpenApiSpecVersion.OpenApi3_2]);

        MvcBodyId(document, "/contexts/mvc-customer").Should().Be("CustomerProfile");
        PropertyNames(document, "CustomerProfile").Should().BeEquivalentTo(["displayName", "loyaltyPoints"]);

        SseDataId(document, "/contexts/sse-customers").Should().Be("CustomerProfileHttp");
        PropertyNames(document, "CustomerProfileHttp").Should().BeEquivalentTo(["display_name", "loyalty_points"]);
    }

    [Fact]
    public void RealDtoNamedLikeTheHttpId_KeepsItsComponent()
    {
        var document = Doc(fixture.Builds[OpenApiSpecVersion.OpenApi3_2]);

        MvcBodyId(document, "/contexts/mvc-order-http-dto").Should().Be("OrderSummaryHttp");
        PropertyNames(document, "OrderSummaryHttp").Should().BeEquivalentTo(["realDtoMarker"]);

        var httpOrder = SseDataId(document, "/contexts/sse-orders");
        httpOrder.Should().NotBe("OrderSummaryHttp").And.NotBe(MvcBodyId(document, "/contexts/mvc-order"));
        PropertyNames(document, httpOrder).Should().BeEquivalentTo(["order_number", "item_count", "customer_note"]);
        PropertyNames(document, MvcBodyId(document, "/contexts/mvc-order")).Should().BeEquivalentTo(["orderNumber", "itemCount", "customerNote"]);
    }

    [Fact]
    public void TypeUsedOnlyByTheHttpContext_KeepsItsUsualId()
    {
        var document = Doc(fixture.Builds[OpenApiSpecVersion.OpenApi3_2]);

        SseDataId(document, "/contexts/sse-labels").Should().Be("HttpOnlyLabel");
        PropertyNames(document, "HttpOnlyLabel").Should().BeEquivalentTo(["label_text"]);
    }

    [Fact]
    public void ClosedGeneric_EachContextHasItsOwnTarget_AndARealDtoKeepsTheHttpName()
    {
        var document = Doc(fixture.Builds[OpenApiSpecVersion.OpenApi3_2]);

        var mvcEnvelope = MvcBodyId(document, "/contexts/mvc-envelope");
        var httpEnvelope = SseDataId(document, "/contexts/sse-envelopes");

        mvcEnvelope.Should().Be("OrderSummaryEnvelope");
        httpEnvelope.Should().NotBe(mvcEnvelope).And.NotBe("OrderSummaryEnvelopeHttp");
        PropertyNames(document, "OrderSummaryEnvelopeHttp").Should().BeEquivalentTo(["realEnvelopeMarker"]);

        PropertyNames(document, mvcEnvelope).Should().BeEquivalentTo(["payload", "sealedAt"]);
        PropertyNames(document, httpEnvelope).Should().BeEquivalentTo(["payload", "sealed_at"]);
        RefsIn(Component(document, mvcEnvelope)).Should().Equal(MvcBodyId(document, "/contexts/mvc-order"));
        RefsIn(Component(document, httpEnvelope)).Should().Equal(SseDataId(document, "/contexts/sse-orders"));
    }

    // ── Row: polymorphic roles in both contexts ─────────────────────────────

    [Fact]
    public void PolymorphicBase_EveryRoleHasATargetPerContext()
    {
        var document = Doc(fixture.Builds[OpenApiSpecVersion.OpenApi3_2]);

        var mvcUnion = MvcBodyId(document, "/contexts/mvc-rebate");
        var httpUnion = SseDataId(document, "/contexts/sse-rebates");
        mvcUnion.Should().Be("Rebate");
        httpUnion.Should().Be("RebateHttp");

        var mvc = Component(document, mvcUnion);
        RefsIn(mvc["oneOf"]).Should().Equal("PercentRebateAsRebate", "RebateDefault");
        MappingIds(mvc).Should().Equal("PercentRebateAsRebate");
        Id(mvc["discriminator"]!["defaultMapping"]!.GetValue<string>()).Should().Be("RebateDefault");

        var http = Component(document, httpUnion);
        RefsIn(http["oneOf"]).Should().Equal("PercentRebateAsRebateHttp", "RebateHttpDefault");
        MappingIds(http).Should().Equal("PercentRebateAsRebateHttp");
        Id(http["discriminator"]!["defaultMapping"]!.GetValue<string>()).Should().Be("RebateHttpDefault");

        PropertyNames(document, "PercentRebateAsRebate").Should().BeEquivalentTo(["$type", "discountPercent", "rebateCode"]);
        PropertyNames(document, "PercentRebateAsRebateHttp").Should().BeEquivalentTo(["$type", "discount_percent", "rebate_code"]);
        PropertyNames(document, "RebateDefault").Should().Contain("rebateCode");
        PropertyNames(document, "RebateHttpDefault").Should().Contain("rebate_code");
    }

    [Fact]
    public void PolymorphicBase_CollidingWithRealDtos_EachUnionLeadsOnlyToItsOwnContext()
    {
        var document = Doc(fixture.Builds[OpenApiSpecVersion.OpenApi3_2]);

        // Real types named like the roles: whichever is generated first keeps the name, the other
        // falls back; each keeps its own content.
        var realDtos = new[]
        {
            (Path: "/contexts/mvc-shipment-role-dtos", Marker: "realVariantMarker"),
            (Path: "/contexts/mvc-shipment-default-dto", Marker: "realDefaultMarker"),
            (Path: "/contexts/mvc-shipment-http-dto", Marker: "realHttpMarker"),
        }.Select(r =>
        {
            var id = MvcBodyId(document, r.Path);
            PropertyNames(document, id).Should().BeEquivalentTo([r.Marker]);
            return id;
        }).ToList();

        var mvcUnion = MvcBodyId(document, "/contexts/mvc-shipment");
        var httpUnion = SseDataId(document, "/contexts/sse-shipments");
        mvcUnion.Should().NotBe(httpUnion);
        realDtos.Should().NotContain([mvcUnion, httpUnion]);

        var mvc = Component(document, mvcUnion);
        var http = Component(document, httpUnion);
        var mvcTargets = RefsIn(mvc["oneOf"]);
        var httpTargets = RefsIn(http["oneOf"]);
        mvcTargets.Should().HaveCount(2).And.NotIntersectWith(httpTargets).And.NotIntersectWith(realDtos);
        httpTargets.Should().HaveCount(2).And.NotIntersectWith(realDtos);

        MappingIds(mvc).Should().BeSubsetOf(mvcTargets);
        MappingIds(http).Should().BeSubsetOf(httpTargets);
        mvcTargets.Should().Contain(Id(mvc["discriminator"]!["defaultMapping"]!.GetValue<string>()));
        httpTargets.Should().Contain(Id(http["discriminator"]!["defaultMapping"]!.GetValue<string>()));

        foreach (var target in mvcTargets)
            PropertyNames(document, target).Should().Contain("trackingCode", because: target);
        foreach (var target in httpTargets)
            PropertyNames(document, target).Should().Contain("tracking_code", because: target);
    }

    // ── Row: the warning about shared types ─────────────────────────────────

    private static readonly string[] SharedTypes =
    [
        "ModernApi.Models.Contexts.CustomerProfile",
        "ModernApi.Models.Contexts.Envelope<ModernApi.Models.Contexts.OrderSummary>",
        "ModernApi.Models.Contexts.ExpressShipment",
        "ModernApi.Models.Contexts.OrderSummary",
        "ModernApi.Models.Contexts.PercentRebate",
        "ModernApi.Models.Contexts.Rebate",
        "ModernApi.Models.Contexts.Shipment",
        "ModernApi.Models.Streaming.StreamItem",
    ];

    [Fact]
    public void DifferentOptions_OneDocumentWarningNamesExactlyTheSharedTypes()
    {
        var warnings = fixture.Builds[OpenApiSpecVersion.OpenApi3_2].Diagnostics.Where(d => d.Code == SharedTypesCode).ToList();

        var warning = warnings.Should().ContainSingle().Subject;
        warning.Location.Should().BeNull(because: "the warning is anchored on the document");
        warning.Subjects.Should().BeEquivalentTo(SharedTypes);
        warning.Subjects.Should().NotContain(s => s.Contains("HttpOnlyLabel") || s.Contains("TreeEvent"),
            because: "a type of one context only is not shared");
    }

    [Fact]
    public void DifferentOptions_AllPathsExcluded_TheWarningStaysOneWithTheSameTypes()
    {
        var build = ContextBuilds.Build(
            ContextBuilds.ProgramCs(httpOptions: SplitContextsFixture.HttpSnakeCase), excludePathPrefixes: ["/"]);

        build.Document["paths"]!.AsObject().Should().BeEmpty();
        build.Diagnostics.Where(d => d.Code == SharedTypesCode).Should().ContainSingle()
            .Which.Subjects.Should().BeEquivalentTo(SharedTypes);
    }

    // ── Row: which differences split the contexts ───────────────────────────

    public static TheoryData<string, string?, string?, bool> Differences => new()
    {
        { "naming policy", null, "o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;", true },
        { "ignore condition", null, "o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;", true },
        { "number handling", null, "o.SerializerOptions.NumberHandling = JsonNumberHandling.WriteAsString;", true },
        { "converters", null, "o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());", true },
        { "equal options",
            "o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;",
            "o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;", false },
        { "dictionary key policy only", null, "o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;", false },
    };

    [Theory]
    [MemberData(nameof(Differences))]
    public void OnlyTheFourSchemaFactors_SplitTheContexts(string difference, string? mvcOptions, string? httpOptions, bool split)
    {
        var build = ContextBuilds.Build(ContextBuilds.ProgramCs(mvcOptions, httpOptions));

        var keys = build.Document["components"]!["schemas"]!.AsObject().Select(p => p.Key).ToList();
        var warnings = build.Diagnostics.Count(d => d.Code == SharedTypesCode);
        if (split)
        {
            keys.Should().Contain("CustomerProfileHttp", because: difference);
            SseDataId(build.Document, "/contexts/sse-customers").Should().Be("CustomerProfileHttp", because: difference);
            warnings.Should().Be(1, because: difference);
        }
        else
        {
            keys.Should().NotContain(k => k.EndsWith("Http", StringComparison.Ordinal) && k != "OrderSummaryHttp"
                                          && k != "ShipmentHttp" && k != "OrderSummaryEnvelopeHttp", because: difference);
            SseDataId(build.Document, "/contexts/sse-customers").Should().Be("CustomerProfile", because: difference);
            warnings.Should().Be(0, because: difference);
        }
    }

    // ── Row: the CLI naming policy with one configured context ──────────────

    [Fact]
    public void CliPolicy_WithOnlyTheHttpContextConfigured_DoesNotReachTheMvcContext()
    {
        var build = ContextBuilds.Build(
            ContextBuilds.ProgramCs(httpOptions: "o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower;"),
            cliPolicy: Core.JsonNamingPolicy.SnakeCaseLower);

        PropertyNames(build.Document, MvcBodyId(build.Document, "/contexts/mvc-customer")).Should().BeEquivalentTo(["displayName", "loyaltyPoints"]);
        PropertyNames(build.Document, SseDataId(build.Document, "/contexts/sse-customers")).Should().BeEquivalentTo(["display-name", "loyalty-points"]);
    }

    [Fact]
    public void CliPolicy_WithOnlyTheMvcContextConfigured_DoesNotReachTheHttpContext()
    {
        var build = ContextBuilds.Build(
            ContextBuilds.ProgramCs(mvcOptions: "o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower;"),
            cliPolicy: Core.JsonNamingPolicy.SnakeCaseLower);

        PropertyNames(build.Document, MvcBodyId(build.Document, "/contexts/mvc-customer")).Should().BeEquivalentTo(["display-name", "loyalty-points"]);
        PropertyNames(build.Document, SseDataId(build.Document, "/contexts/sse-customers")).Should().BeEquivalentTo(["displayName", "loyaltyPoints"]);
    }

    [Fact]
    public void CliPolicy_WithoutAnyJsonOptions_AppliesToBothContexts()
    {
        var build = ContextBuilds.Build(ContextBuilds.ProgramCs(), cliPolicy: Core.JsonNamingPolicy.SnakeCaseLower);

        MvcBodyId(build.Document, "/contexts/mvc-customer").Should().Be("CustomerProfile");
        SseDataId(build.Document, "/contexts/sse-customers").Should().Be("CustomerProfile");
        PropertyNames(build.Document, "CustomerProfile").Should().BeEquivalentTo(["display_name", "loyalty_points"]);
        build.Diagnostics.Should().NotContain(d => d.Code == SharedTypesCode);
    }

    // ── Row: SC-003 in both contexts ────────────────────────────────────────

    private static readonly CustomerProfile Customer = new() { DisplayName = "Ann", LoyaltyPoints = 5 };

    private static void SnakeCaseHttp(Microsoft.AspNetCore.Http.Json.JsonOptions options) =>
        options.SerializerOptions.PropertyNamingPolicy = StjNamingPolicy.SnakeCaseLower;

    private static async IAsyncEnumerable<T> Sequence<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void MvcBody_PassesItsOwnSchema_AndFailsTheHttpOne(OpenApiSpecVersion version)
    {
        var conformance = fixture.Conformance[version];
        var body = StjWire.Serialize(Customer, StjWire.Mvc());

        var own = conformance.ValidateComponent("CustomerProfile", body);
        own.IsValid.Should().BeTrue(because: string.Join("; ", own.Errors));
        conformance.ValidateComponent("CustomerProfileHttp", body).IsValid
            .Should().BeFalse(because: "the HTTP schema requires the snake_case names of the HTTP wire");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task EventData_WrittenWithHttpOptions_PassesTheHttpSchema(OpenApiSpecVersion version)
    {
        var document = Doc(fixture.Builds[version]);
        var response = await SseWire.ExecuteAsync(
            TypedResults.ServerSentEvents(Sequence(Customer)), SnakeCaseHttp, TestContext.Current.CancellationToken);

        var written = response.Events.Should().ContainSingle().Subject;
        var result = fixture.Conformance[version].ValidateSchema(ContentSchema(document, "/contexts/sse-customers"), written.ParseJsonData());
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task PolymorphicEventData_PassesTheHttpUnionWithOneMatch(OpenApiSpecVersion version)
    {
        var document = Doc(fixture.Builds[version]);
        var response = await SseWire.ExecuteAsync(
            TypedResults.ServerSentEvents(Sequence<Rebate>(new PercentRebate { RebateCode = "R", DiscountPercent = 15 })),
            SnakeCaseHttp, TestContext.Current.CancellationToken);

        var data = response.Events.Should().ContainSingle().Subject.ParseJsonData();
        data!["discount_percent"].Should().NotBeNull(because: "the wire sample is the HTTP context's");

        var union = SseDataId(document, "/contexts/sse-rebates");
        var result = fixture.Conformance[version].ValidateComponent(union, data);
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
        fixture.Conformance[version].CountMatchingOneOfBranches(union, data).Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void PolymorphicMvcBody_PassesTheMvcUnionWithOneMatch(OpenApiSpecVersion version)
    {
        var body = StjWire.Serialize(new PercentRebate { RebateCode = "R", DiscountPercent = 15 }, typeof(Rebate), StjWire.Mvc());

        var result = fixture.Conformance[version].ValidateComponent("Rebate", body);
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
        fixture.Conformance[version].CountMatchingOneOfBranches("Rebate", body).Should().Be(1);
    }
}
