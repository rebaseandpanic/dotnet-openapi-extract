using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Metadata;

/// <summary>ModernApi with every document metadata option, in every version, plus an all-excluded build.</summary>
public sealed class DocumentMetadataFixture : IDisposable
{
    public const string Dialect31 = "https://spec.openapis.org/oas/3.1/dialect/base";
    public const string Dialect32 = "https://spec.openapis.org/oas/3.2/dialect/2025-09-17";

    private readonly TempDirectory _pathBase = new();

    public DocumentMetadataFixture()
    {
        File.WriteAllText(Path.Combine(_pathBase.Path, "Program.cs"), """
            var app = builder.Build();
            app.UsePathBase("/base");
            app.MapControllers();
            app.Run();
            """);

        foreach (var version in VersionedDocumentHarness.Versions)
            Builds[version] = Build(version, null);
        Excluded = Build(OpenApiSpecVersion.OpenApi3_0, ["/"]);
    }

    public static string DialectOf(OpenApiSpecVersion version) =>
        version == OpenApiSpecVersion.OpenApi3_2 ? Dialect32 : Dialect31;

    private (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(OpenApiSpecVersion version, IReadOnlyList<string>? exclude)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            SourceRoot          = _pathBase.Path,
            PathBaseEmission    = PathBaseEmission.ServersEntry,
            OpenApiVersion      = version,
            OnDiagnostic        = onDiagnostic,
            ExcludePathPrefixes = exclude,
            Summary             = "Modern API summary",
            LicenseName         = "MIT License",
            LicenseIdentifier   = "MIT",
            Servers             = ["https://prod.example.com", "https://staging.example.com"],
            ServerNames         = ["prod", "staging"],
            SelfUrl             = "https://example.com/openapi.json",
            JsonSchemaDialect   = DialectOf(version),
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];

    public (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Excluded { get; }

    public void Dispose() => _pathBase.Dispose();
}

/// <summary>
/// Document metadata options of OpenAPI 3.1/3.2 — summary, license identifier, server names, <c>$self</c>,
/// JSON Schema dialect — in the form of each version, with one warning per field and place where a
/// version cannot hold it; wrong combinations are configuration errors.
/// </summary>
public class DocumentMetadataOptionsTests(DocumentMetadataFixture fixture) : IClassFixture<DocumentMetadataFixture>
{
    private static ExtractionDiagnostic? At(IReadOnlyList<ExtractionDiagnostic> diagnostics, string location) =>
        diagnostics.Where(d => d.Location == location).Should().ContainSingle(because: location).Subject;

    [Fact]
    public void For30_TheFieldsTakeThe30Form_OneWarningEach()
    {
        var (document, diagnostics) = fixture.Builds[OpenApiSpecVersion.OpenApi3_0];

        document["info"]!.AsObject().ContainsKey("summary").Should().BeFalse();
        document["info"]!["license"]!.ToJsonString().Should().Be("""{"name":"MIT License","x-oai-license-identifier":"MIT"}""");
        document["servers"]![0]!["x-oai-name"]!.GetValue<string>().Should().Be("prod");
        document["servers"]![1]!["x-oai-name"]!.GetValue<string>().Should().Be("staging");
        document["x-oai-$self"]!.GetValue<string>().Should().Be("https://example.com/openapi.json");
        document.AsObject().ContainsKey("jsonSchemaDialect").Should().BeFalse();

        At(diagnostics, "#/info/summary")!.Code.Should().Be(ExtractionDiagnosticCodes.DocumentSummaryOmitted);
        At(diagnostics, "#/info/license/x-oai-license-identifier")!.Code.Should().Be(ExtractionDiagnosticCodes.DocumentLicenseIdentifierMovedToExtension);
        At(diagnostics, "#/servers/0/x-oai-name")!.Subjects.Should().Equal("prod");
        At(diagnostics, "#/servers/1/x-oai-name")!.Subjects.Should().Equal("staging");
        At(diagnostics, "#/x-oai-$self")!.Code.Should().Be(ExtractionDiagnosticCodes.DocumentSelfMovedToExtension);
        At(diagnostics, "#/jsonSchemaDialect")!.Code.Should().Be(ExtractionDiagnosticCodes.DocumentJsonSchemaDialectOmitted);
    }

    [Fact]
    public void For31_SummaryLicenseAndDialectAreNative_ServerNamesAndSelfMoved()
    {
        var (document, diagnostics) = fixture.Builds[OpenApiSpecVersion.OpenApi3_1];

        document["info"]!["summary"]!.GetValue<string>().Should().Be("Modern API summary");
        document["info"]!["license"]!.ToJsonString().Should().Be("""{"name":"MIT License","identifier":"MIT"}""");
        document["jsonSchemaDialect"]!.GetValue<string>().Should().Be(DocumentMetadataFixture.Dialect31);
        document["servers"]![0]!["x-oai-name"]!.GetValue<string>().Should().Be("prod");
        document["x-oai-$self"]!.GetValue<string>().Should().Be("https://example.com/openapi.json");

        diagnostics.Should().NotContain(d => d.Location == "#/info/summary" || d.Location == "#/jsonSchemaDialect"
                                             || d.Location == "#/info/license/x-oai-license-identifier");
        At(diagnostics, "#/servers/0/x-oai-name")!.Code.Should().Be(ExtractionDiagnosticCodes.DocumentServerNameMovedToExtension);
        At(diagnostics, "#/x-oai-$self")!.Code.Should().Be(ExtractionDiagnosticCodes.DocumentSelfMovedToExtension);
    }

    [Fact]
    public void For32_EveryFieldIsNative_NoWarnings()
    {
        var (document, diagnostics) = fixture.Builds[OpenApiSpecVersion.OpenApi3_2];

        document["info"]!["summary"]!.GetValue<string>().Should().Be("Modern API summary");
        document["info"]!["license"]!["identifier"]!.GetValue<string>().Should().Be("MIT");
        document["servers"]![0]!["name"]!.GetValue<string>().Should().Be("prod");
        document["$self"]!.GetValue<string>().Should().Be("https://example.com/openapi.json");
        document["jsonSchemaDialect"]!.GetValue<string>().Should().Be(DocumentMetadataFixture.Dialect32);
        diagnostics.Should().NotContain(d => d.Code.StartsWith("document.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void ServerNames_BindByPosition_ThePathBaseServerGetsNone(OpenApiSpecVersion version)
    {
        var servers = fixture.Builds[version].Document["servers"]!.AsArray();

        servers.Select(s => s!["url"]!.GetValue<string>()).Should().Equal("https://prod.example.com", "https://staging.example.com", "/base");
        var name = version == OpenApiSpecVersion.OpenApi3_2 ? "name" : "x-oai-name";
        servers[2]!.AsObject().ContainsKey(name).Should().BeFalse();
    }

    [Fact]
    public void MetadataWarnings_AreKept_WhenEveryPathIsExcluded()
    {
        var codes = fixture.Excluded.Diagnostics.Select(d => d.Code).ToList();

        codes.Should().Contain([
            ExtractionDiagnosticCodes.DocumentSummaryOmitted,
            ExtractionDiagnosticCodes.DocumentLicenseIdentifierMovedToExtension,
            ExtractionDiagnosticCodes.DocumentSelfMovedToExtension,
            ExtractionDiagnosticCodes.DocumentJsonSchemaDialectOmitted,
        ]);
        codes.Count(c => c == ExtractionDiagnosticCodes.DocumentServerNameMovedToExtension).Should().Be(2);
    }

    // ── Configuration errors ───────────────────────────────────────────────────

    private static OpenApiDocumentOptions Options(OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1) => new()
    {
        AssemblyPath   = TestPaths.ModernApiDll,
        OpenApiVersion = version,
    };

    public static TheoryData<string, OpenApiDocumentOptions> WrongCombinations => new()
    {
        { "identifier and url", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, LicenseName = "MIT", LicenseIdentifier = "MIT", LicenseUrl = "https://opensource.org/licenses/MIT" } },
        { "identifier without name", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, LicenseIdentifier = "MIT" } },
        { "url without name", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, LicenseUrl = "https://opensource.org/licenses/MIT" } },
        { "fewer names than servers", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, Servers = ["https://a.example.com", "https://b.example.com"], ServerNames = ["a"] } },
        { "names without servers", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, ServerNames = ["a"] } },
        { "empty name", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, Servers = ["https://a.example.com"], ServerNames = [" "] } },
        { "repeated name", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, Servers = ["https://a.example.com", "https://b.example.com"], ServerNames = ["a", "a"] } },
        { "self with a fragment", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, SelfUrl = "https://example.com/openapi.json#top" } },
        { "self not a URI reference", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, SelfUrl = "http://[bad" } },
        { "self with raw spaces", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, SelfUrl = "not a uri" } },
        { "self with a bad escape", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, SelfUrl = "https://example.com/a%zz" } },
        { "unsupported dialect", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, JsonSchemaDialect = "https://json-schema.org/draft/2020-12/schema" } },
        { "3.2 dialect for 3.1", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_1, JsonSchemaDialect = DocumentMetadataFixture.Dialect32 } },
        { "3.1 dialect for 3.2", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_2, JsonSchemaDialect = DocumentMetadataFixture.Dialect31 } },
        { "unsupported dialect for 3.0", new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, OpenApiVersion = OpenApiSpecVersion.OpenApi3_0, JsonSchemaDialect = "https://example.com/dialect" } },
    };

    [Theory]
    [MemberData(nameof(WrongCombinations))]
    public void WrongCombination_IsAConfigurationError(string _, OpenApiDocumentOptions options)
    {
        var build = () => OpenApiDocumentBuilder.Build(options);

        build.Should().Throw<OpenApiConfigurationException>();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, DocumentMetadataFixture.Dialect31, true)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, DocumentMetadataFixture.Dialect31, false)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, DocumentMetadataFixture.Dialect32, false)]
    public void Dialect_IsWrittenForItsVersion_OmittedWithOneWarningFor30(OpenApiSpecVersion version, string dialect, bool written)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath      = TestPaths.ModernApiDll,
            OpenApiVersion    = version,
            JsonSchemaDialect = dialect,
            OnDiagnostic      = onDiagnostic,
        });
        var json = JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;

        json.AsObject().ContainsKey("jsonSchemaDialect").Should().Be(written);
        diagnostics.Count(d => d.Code == ExtractionDiagnosticCodes.DocumentJsonSchemaDialectOmitted).Should().Be(written ? 0 : 1);
    }

    [Fact]
    public void WithoutTheOption_NoDialectIsWritten()
    {
        var document = OpenApiDocumentBuilder.Build(Options());
        var json = JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!;

        json.AsObject().ContainsKey("jsonSchemaDialect").Should().BeFalse();
    }

    [Theory]
    [InlineData("openapi.json")]
    [InlineData("/specs/openapi.json")]
    [InlineData("https://example.com/specs/openapi.json?v=2")]
    public void SelfUrl_RelativeOrAbsolute_IsAccepted(string self)
    {
        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_2,
            SelfUrl        = self,
        });
        var json = JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_2, CancellationToken.None).GetAwaiter().GetResult())!;

        json["$self"]!.GetValue<string>().Should().Be(self);
    }

    [Fact]
    public void SelfUrl_WithAValidPercentEncoding_IsAccepted()
    {
        // Accepted as well formed; how the escape is written is the library's (it writes Uri.ToString()).
        var build = () => OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_2,
            SelfUrl        = "https://example.com/a%20b/openapi.json",
        });

        build.Should().NotThrow();
    }
}
