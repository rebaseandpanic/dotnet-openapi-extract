using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Builds ModernApi with a Program.cs that turns on every document-wide mechanism (global media
/// types, ProblemDetails, global response header, security cleanup), per target version, once
/// more with the consumers path excluded, and with validation per version.
/// </summary>
public sealed class AnyMethodConsumersFixture : IDisposable
{
    private const string Program =
        """
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute("application/json", "application/xml")));
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.Use(async (ctx, next) => { ctx.Response.Headers.Append("X-Trace-Id", "1"); await next(); });
        app.MapControllers();
        app.Run();
        """;

    private readonly TempDirectory _source = new();

    public AnyMethodConsumersFixture()
    {
        File.WriteAllText(Path.Combine(_source.Path, "Program.cs"), Program);
        File.WriteAllText(
            Path.Combine(_source.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        foreach (var version in VersionedDocumentHarness.Versions)
        {
            Documents[version] = Serialize(OpenApiDocumentBuilder.Build(Options(version, null)), version);

            var built = OpenApiDocumentBuilder.BuildWithValidation(
                Options(version, null), new ValidationContext(), out var validation);
            Validated[version] = (Serialize(built, version), validation);
        }

        Excluded30 = Serialize(
            OpenApiDocumentBuilder.Build(Options(OpenApiSpecVersion.OpenApi3_0, ["/consumers"])),
            OpenApiSpecVersion.OpenApi3_0);
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, ValidationResult Result)> Validated { get; } = [];

    public JsonNode Excluded30 { get; }

    public void Dispose() => _source.Dispose();

    private OpenApiDocumentOptions Options(OpenApiSpecVersion version, IReadOnlyList<string>? excluded) => new()
    {
        AssemblyPath        = TestPaths.ModernApiDll,
        XmlPath             = TestPaths.ModernApiXml,
        SourceRoot          = _source.Path,
        OpenApiVersion      = version,
        ExcludePathPrefixes = excluded,
        OnDiagnostic        = _ => { },
    };

    private static JsonNode Serialize(OpenApiDocument document, OpenApiSpecVersion version) =>
        VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
}

/// <summary>
/// Operations with any HTTP method, in any root, are seen by every consumer of operations: the
/// document-wide build mechanisms and the validation rules, with pointers of the actual output.
/// </summary>
public class AnyMethodOperationConsumersTests(AnyMethodConsumersFixture fixture) : IClassFixture<AnyMethodConsumersFixture>
{
    public static TheoryData<OpenApiSpecVersion> Versions => [.. VersionedDocumentHarness.Versions];

    private static JsonNode? Moved(JsonNode document, string path, string method, OpenApiSpecVersion version)
    {
        var item = document["paths"]![path]!;
        return method switch
        {
            "QUERY" when version == OpenApiSpecVersion.OpenApi3_2 => item["query"],
            _ when version == OpenApiSpecVersion.OpenApi3_2 => item["additionalOperations"]![method],
            _ => item["x-oai-additionalOperations"]![method],
        };
    }

    // ── Build-time consumers ─────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void QueryAndLink_GetTheSameDocumentWideTreatmentAsGetAndPost(OpenApiSpecVersion version)
    {
        var document = fixture.Documents[version];
        var get = document["paths"]!["/consumers/search"]!["get"]!;
        var post = document["paths"]!["/consumers/link"]!["post"]!;

        // The mechanisms really ran on the standard twins…
        get["responses"]!["500"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
            .Should().Be("#/components/schemas/ProblemDetails");
        get["responses"]!["200"]!["content"]!.AsObject().ContainsKey("application/xml").Should().BeTrue();
        get["responses"]!["200"]!["headers"]!.AsObject().ContainsKey("X-Trace-Id").Should().BeTrue();
        get.AsObject().ContainsKey("security").Should().BeFalse(because: "the undeclared scheme is omitted");

        // …and give the same result for QUERY and LINK (the pairs differ only in the method).
        JsonNode.DeepEquals(Moved(document, "/consumers/search", "QUERY", version), get).Should().BeTrue();
        JsonNode.DeepEquals(Moved(document, "/consumers/link", "LINK", version), post).Should().BeTrue();
    }

    [Fact]
    public void ExcludedPath_RemovesQueryAndLinkOperations()
    {
        var paths = fixture.Excluded30["paths"]!.AsObject();
        paths.ContainsKey("/consumers/search").Should().BeFalse();
        paths.ContainsKey("/consumers/link").Should().BeFalse();
    }

    // ── Validation pointers on built documents ───────────────────────────────

    [Theory]
    [MemberData(nameof(Versions))]
    public void ViolationPointer_FollowsTheActualOutput(OpenApiSpecVersion version)
    {
        var (document, result) = fixture.Validated[version];
        var expected = version == OpenApiSpecVersion.OpenApi3_2
            ? "#/paths/~1additional-methods~1search/query"
            : "#/paths/~1additional-methods~1search/x-oai-additionalOperations/QUERY";

        result.SkippedRules.Should().NotContain("operation.operation-id");
        result.Violations.Should().Contain(v => v.RuleId == "operation.operation-id" && v.JsonPointer == expected);
        JsonReferences.Resolve(document, expected).Should().NotBeNull();
    }
}
