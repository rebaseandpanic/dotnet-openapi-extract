using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Contexts;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.Streaming;
using Microsoft.OpenApi;
using ModernApi.Models.Contexts;
using Xunit;
using StjNamingPolicy = System.Text.Json.JsonNamingPolicy;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>ModernApi for every version (diagnostics collected) and with HTTP-only snake_case for 3.2.</summary>
public sealed class TypedResultsFixture
{
    public TypedResultsFixture()
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
            Builds[version] = new CollectedBuild(
                VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None).GetAwaiter().GetResult(),
                diagnostics);
        }

        Split = ContextBuilds.Build(ContextBuilds.ProgramCs(httpOptions: SplitContextsFixture.HttpSnakeCase));
        SplitConformance = SchemaConformance.For(Split.Document);
    }

    public Dictionary<OpenApiSpecVersion, CollectedBuild> Builds { get; } = [];

    public CollectedBuild Split { get; }

    public SchemaConformance SplitConformance { get; }
}

/// <summary>
/// Typed results declare their own responses: <c>Results&lt;…&gt;</c> by its variants, each result with
/// a known status by that status and its body (in the HTTP serialization context), status-only
/// results without a schema; an explicit declaration wins; a result of unknown status is reported.
/// </summary>
public class TypedResultsResponseTests(TypedResultsFixture fixture) : IClassFixture<TypedResultsFixture>
{
    private const string UnknownCode = ExtractionDiagnosticCodes.ResponseResultStatusUnknown;

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private static JsonObject Responses(JsonNode document, string path, string method = "get") =>
        document["paths"]![path]![method]!["responses"]!.AsObject();

    private static string? BodyRef(JsonObject responses, string code, string mediaType = "application/json") =>
        responses[code]!["content"]?[mediaType]?["schema"]?["$ref"]?.GetValue<string>();

    private static IEnumerable<ExtractionDiagnostic> Warnings(CollectedBuild build, string location) =>
        build.Diagnostics.Where(d => d.Location == location);

    // ── Row: each class of result ───────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Union_GivesOneResponsePerVariant(OpenApiSpecVersion version)
    {
        var responses = Responses(fixture.Builds[version].Document, "/typed-results/union");

        responses.Select(r => r.Key).Should().BeEquivalentTo(["200", "404", "400"]);
        BodyRef(responses, "200").Should().Be("#/components/schemas/ResultItem");
        responses["404"]!["content"].Should().BeNull(because: "NotFound has no body");
        BodyRef(responses, "400").Should().Be("#/components/schemas/ProblemDetails");

        var inTask = Responses(fixture.Builds[version].Document, "/typed-results/union-task");
        inTask.Select(r => r.Key).Should().BeEquivalentTo(["200", "404"]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ResultsWithKnownStatus_GiveThatStatusAndTheirBody(OpenApiSpecVersion version)
    {
        var document = fixture.Builds[version].Document;

        var ok = Responses(document, "/typed-results/ok");
        ok.Select(r => r.Key).Should().Equal("200");
        BodyRef(ok, "200").Should().Be("#/components/schemas/ResultItem");

        var created = Responses(document, "/typed-results/created", "post");
        created.Select(r => r.Key).Should().Equal("201");
        BodyRef(created, "201").Should().Be("#/components/schemas/ResultItem");

        var validation = Responses(document, "/typed-results/validation", "post");
        validation.Select(r => r.Key).Should().Equal("400");
        BodyRef(validation, "400", "application/problem+json").Should().Be("#/components/schemas/HttpValidationProblemDetails");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void StatusOnlyResults_GiveTheirStatusWithoutSchemaOrWarning(OpenApiSpecVersion version)
    {
        var build = fixture.Builds[version];

        var noContent = Responses(build.Document, "/typed-results/no-content", "delete");
        noContent.Select(r => r.Key).Should().Equal("204");
        noContent["204"]!["content"].Should().BeNull();

        var unauthorized = Responses(build.Document, "/typed-results/unauthorized");
        unauthorized.Select(r => r.Key).Should().Equal("401");
        unauthorized["401"]!["content"].Should().BeNull();

        foreach (var location in new[]
                 {
                     "DELETE /typed-results/no-content", "GET /typed-results/unauthorized", "GET /typed-results/union",
                     "GET /typed-results/ok", "POST /typed-results/created", "POST /typed-results/validation",
                 })
            Warnings(build, location).Should().BeEmpty(because: location);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Components_HaveNoResultWrappers(OpenApiSpecVersion version)
    {
        var keys = fixture.Builds[version].Document["components"]!["schemas"]!.AsObject().Select(p => p.Key).ToList();

        keys.Should().NotContain(k => k == "IResult" || k.EndsWith("Ok", StringComparison.Ordinal)
                                      || k.Contains("Results", StringComparison.Ordinal) || k.Contains("NotFound", StringComparison.Ordinal)
                                      || k.Contains("HttpResult", StringComparison.Ordinal) || k.Contains("CustomResult", StringComparison.Ordinal)
                                      || k == "NoContent" || k == "Created" || k == "ResultItemCreated" || k == "ValidationProblem");
    }

    // ── Row: explicit declarations and unknown statuses ─────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ExplicitDeclaration_WinsOverTheInferredResponse(OpenApiSpecVersion version)
    {
        var responses = Responses(fixture.Builds[version].Document, "/typed-results/explicit");

        responses.Select(r => r.Key).Should().BeEquivalentTo(["200", "404"]);
        BodyRef(responses, "404").Should().Be("#/components/schemas/ResultError",
            because: "the declared type wins over NotFound<ResultItem>");
        BodyRef(responses, "200").Should().Be("#/components/schemas/ResultItem");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ResultOfUnknownStatus_IsA200WithoutSchemaAndOneWarning(OpenApiSpecVersion version)
    {
        var build = fixture.Builds[version];

        foreach (var (path, subject) in new[]
                 {
                     ("/typed-results/untyped", "Microsoft.AspNetCore.Http.IResult"),
                     ("/typed-results/custom", "ModernApi.Controllers.CustomResult"),
                     ("/typed-results/json", "Microsoft.AspNetCore.Http.HttpResults.JsonHttpResult<ModernApi.Models.TypedResults.ResultItem>"),
                 })
        {
            var responses = Responses(build.Document, path);
            responses.Select(r => r.Key).Should().Equal(["200"], because: path);
            responses["200"]!["content"].Should().BeNull(because: path);

            var warning = Warnings(build, $"GET {path}").Should().ContainSingle(because: path).Subject;
            warning.Code.Should().Be(UnknownCode);
            warning.Subjects.Should().Equal(subject);
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ResultOfUnknownStatus_WithADeclaredResponse_UsesItWithoutWarning(OpenApiSpecVersion version)
    {
        var build = fixture.Builds[version];
        var responses = Responses(build.Document, "/typed-results/untyped-declared");

        BodyRef(responses, "200").Should().Be("#/components/schemas/ResultItem");
        Warnings(build, "GET /typed-results/untyped-declared").Should().BeEmpty();
    }

    // ── Row: the body is in the HTTP serialization context ──────────────────

    private static readonly CustomerProfile Customer = new() { DisplayName = "Ann", LoyaltyPoints = 5 };

    [Fact]
    public void OkBody_WithDifferentOptions_ReferencesTheHttpSchema()
    {
        var document = fixture.Split.Document;

        BodyRef(Responses(document, "/contexts/result-customer"), "200").Should().Be("#/components/schemas/CustomerProfileHttp");
        BodyRef(Responses(document, "/contexts/mvc-customer"), "200").Should().Be("#/components/schemas/CustomerProfile");

        // A body declared on an IResult action is written by the result too: HTTP names.
        BodyRef(Responses(document, "/typed-results/untyped-declared"), "200").Should().Be("#/components/schemas/ResultItem");
        document["components"]!["schemas"]!["ResultItem"]!["properties"]!.AsObject().Select(p => p.Key)
            .Should().Equal("item_name");
    }

    [Fact]
    public void OkBody_WrittenWithHttpOptions_PassesItsSchema_AndWithMvcOptionsFails()
    {
        var conformance = fixture.SplitConformance;

        var http = StjWire.Serialize(Customer, StjWire.Http(o => o.PropertyNamingPolicy = StjNamingPolicy.SnakeCaseLower));
        var passed = conformance.ValidateComponent("CustomerProfileHttp", http);
        passed.IsValid.Should().BeTrue(because: string.Join("; ", passed.Errors));

        conformance.ValidateComponent("CustomerProfileHttp", StjWire.Serialize(Customer, StjWire.Mvc())).IsValid
            .Should().BeFalse(because: "the result body is the HTTP wire, not the MVC one");
    }
}
