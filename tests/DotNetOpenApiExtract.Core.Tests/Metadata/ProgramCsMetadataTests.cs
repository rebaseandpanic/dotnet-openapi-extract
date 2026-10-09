using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Metadata;

/// <summary>ModernApi with a Program.cs declaring info, license and tag metadata, in every version.</summary>
public sealed class ProgramCsMetadataFixture : IDisposable
{
    private const string Program = """
        const string Summary = "From a constant";
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Modern",
                Version = "v1",
                Summary = Summary,
                License = new OpenApiLicense { Name = "Apache 2.0", Identifier = "Apache-2.0" },
            });
            c.SwaggerDoc("v2", new OpenApiInfo { Title = "Modern", Version = "v2", Summary = "Second document" });
            c.AddTag(new OpenApiTag { Name = "SchemaKeywords", Summary = "Keywords", Kind = "nav", Parent = new OpenApiTagReference("Streaming") });
        });
        """;

    private readonly TempDirectory _directory = new();

    public ProgramCsMetadataFixture()
    {
        File.WriteAllText(Path.Combine(_directory.Path, "Program.cs"), Program);
        foreach (var version in VersionedDocumentHarness.Versions)
            Builds[version] = Build(_directory.Path, version);
    }

    public static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(
        string sourceRoot, OpenApiSpecVersion version,
        string? licenseUrl = null, string? summary = null, string? licenseName = null)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            XmlPath        = TestPaths.ModernApiXml,
            SourceRoot     = sourceRoot,
            OpenApiVersion = version,
            OnDiagnostic   = onDiagnostic,
            LicenseUrl     = licenseUrl,
            Summary        = summary,
            LicenseName    = licenseName,
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];

    public string SourceRoot => _directory.Path;

    public void Dispose() => _directory.Dispose();
}

/// <summary>
/// <c>Program.cs</c> metadata — <c>OpenApiInfo.Summary</c>, <c>OpenApiLicense</c>, OpenAPI 3.2 tag
/// fields — read from literals and constants, merged with the options field by field (the options
/// first); code is never run.
/// </summary>
public class ProgramCsMetadataTests(ProgramCsMetadataFixture fixture) : IClassFixture<ProgramCsMetadataFixture>
{
    private static JsonNode? Resolve(JsonNode root, string pointer) =>
        pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))
            .Aggregate((JsonNode?)root, (node, segment) => node switch
            {
                JsonObject o => o[segment],
                JsonArray a when int.TryParse(segment, out var i) && i < a.Count => a[i],
                _ => null,
            });

    private static JsonObject Tag(JsonNode document, string name) =>
        document["tags"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == name)!.AsObject();

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void InfoAndLicense_FromProgramCs_TakeTheFormOfTheVersion(OpenApiSpecVersion version)
    {
        var (document, diagnostics) = fixture.Builds[version];
        var info = document["info"]!.AsObject();

        if (version == OpenApiSpecVersion.OpenApi3_0)
        {
            info.ContainsKey("summary").Should().BeFalse();
            info["license"]!.ToJsonString().Should().Be("""{"name":"Apache 2.0","x-oai-license-identifier":"Apache-2.0"}""");
            diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.DocumentSummaryOmitted);
        }
        else
        {
            info["summary"]!.GetValue<string>().Should().Be("From a constant", because: "the first SwaggerDoc that sets it, as for externalDocs");
            info["license"]!.ToJsonString().Should().Be("""{"name":"Apache 2.0","identifier":"Apache-2.0"}""");
        }
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void TagFields_AreNativeIn32_AndExtensionsWithOneWarningEachBefore(OpenApiSpecVersion version)
    {
        var (document, diagnostics) = fixture.Builds[version];
        var tag = Tag(document, "SchemaKeywords");

        if (version == OpenApiSpecVersion.OpenApi3_2)
        {
            tag["summary"]!.GetValue<string>().Should().Be("Keywords");
            tag["kind"]!.GetValue<string>().Should().Be("nav");
            tag["parent"]!.GetValue<string>().Should().Be("Streaming");
            diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.DocumentTagFieldMovedToExtension);
            return;
        }

        tag["x-oas-summary"]!.GetValue<string>().Should().Be("Keywords");
        tag["x-oas-kind"]!.GetValue<string>().Should().Be("nav");
        tag["x-oas-parent"]!.GetValue<string>().Should().Be("Streaming");
        var warnings = diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.DocumentTagFieldMovedToExtension).ToList();
        warnings.Select(w => w.Feature).Should().BeEquivalentTo(["tag.summary", "tag.parent", "tag.kind"]);
        foreach (var warning in warnings)
        {
            warning.Subjects.Should().Equal("SchemaKeywords");
            Resolve(document, warning.Location!).Should().NotBeNull(because: $"{warning.Location} is in the output");
        }
    }

    [Fact]
    public void Options_WinFieldByField_AnOptionUrlReplacesTheProgramCsIdentifier()
    {
        var (document, diagnostics) = ProgramCsMetadataFixture.Build(fixture.SourceRoot, OpenApiSpecVersion.OpenApi3_1,
            licenseUrl: "https://www.apache.org/licenses/LICENSE-2.0", summary: "From the option");

        document["info"]!["summary"]!.GetValue<string>().Should().Be("From the option");
        document["info"]!["license"]!.ToJsonString().Should().Be(
            """{"name":"Apache 2.0","url":"https://www.apache.org/licenses/LICENSE-2.0"}""",
            because: "the name comes from Program.cs, the option URL replaces its identifier without an error");
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.DocumentSummaryOmitted);
    }

    [Fact]
    public void IdentifierAndUrl_InProgramCs_IsAConfigurationError()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), """
            builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo
            {
                License = new OpenApiLicense { Name = "MIT", Identifier = "MIT", Url = new Uri("https://opensource.org/licenses/MIT") },
            }));
            """);

        var build = () => ProgramCsMetadataFixture.Build(directory.Path, OpenApiSpecVersion.OpenApi3_1);

        build.Should().Throw<OpenApiConfigurationException>();
    }

    [Fact]
    public void NonLiteralValues_AreNotWritten_AndProgramCsCodeIsNeverRun()
    {
        using var directory = new TempDirectory();
        var marker = Path.Combine(directory.Path, "marker.txt");
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), $$"""
            string MakeSummary() { System.IO.File.WriteAllText(@"{{marker}}", "ran"); return "Made"; }
            builder.Services.AddSwaggerGen(c =>
            {
                System.IO.File.WriteAllText(@"{{marker}}", "ran");
                c.SwaggerDoc("v1", new OpenApiInfo { Summary = MakeSummary() });
                c.AddTag(new OpenApiTag { Name = "SchemaKeywords", Parent = new OpenApiTagReference(cfg.Parent), Kind = cfg.Kind });
            });
            """);

        var (document, _) = ProgramCsMetadataFixture.Build(directory.Path, OpenApiSpecVersion.OpenApi3_2);

        File.Exists(marker).Should().BeFalse(because: "the source is read, never compiled and run");
        document["info"]!.AsObject().ContainsKey("summary").Should().BeFalse();
        var tag = Tag(document, "SchemaKeywords");
        tag.ContainsKey("parent").Should().BeFalse();
        tag.ContainsKey("kind").Should().BeFalse();
    }
}
