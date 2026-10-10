using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Metadata;

/// <summary>ModernApi with a Program.cs that declares every object in the target-typed form <c>new() { … }</c>.</summary>
public sealed class TargetTypedProgramCsFixture : IDisposable
{
    private const string Program = """
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title = "Modern",
                Version = "v1",
                Summary = "Target-typed",
                License = new() { Name = "MIT", Url = new("https://opensource.org/licenses/MIT") },
                ExternalDocs = new() { Url = new("https://docs.example.com"), Description = "Docs" },
            });
            c.AddTag(new() { Name = "SchemaKeywords", ExternalDocs = new() { Url = new("https://tags.example.com") } });
            c.AddSecurityDefinition("ApiKey", new() { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Api-Key" });
            c.AddSecurityDefinition("Bearer", new() { Type = SecuritySchemeType.Http, Scheme = "bearer" });
            c.AddSecurityDefinition("OAuth", new()
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new() { ClientCredentials = new() { TokenUrl = new("https://auth.example.com/token"), Scopes = new() { ["read"] = "Read" } } },
            });
            c.AddSecurityRequirement(new() { { new() { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }, [] } });
            c.AddSecurityRequirement(document => new() { [new("Bearer", document)] = [], [new("OAuth", document)] = ["read"] });
        });
        """;

    private readonly TempDirectory _directory = new();

    public TargetTypedProgramCsFixture()
    {
        File.WriteAllText(Path.Combine(_directory.Path, "Program.cs"), Program);
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                SourceRoot     = _directory.Path,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Builds[version] = (JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
        }
    }

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];

    public void Dispose() => _directory.Dispose();
}

/// <summary>
/// The target-typed <c>new() { … }</c> gives the same document as <c>new OpenApiInfo { … }</c> and the
/// other explicit forms: the form of the code does not change what Swashbuckle serves, so it does not
/// change the document. A form the extractor cannot read is reported, not lost silently.
/// </summary>
public class TargetTypedProgramCsTests(TargetTypedProgramCsFixture fixture) : IClassFixture<TargetTypedProgramCsFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Info_License_And_ExternalDocs_AreRead(OpenApiSpecVersion version)
    {
        var document = fixture.Builds[version].Document;

        document["info"]!["license"]!.ToJsonString().Should().Be("""{"name":"MIT","url":"https://opensource.org/licenses/MIT"}""");
        document["externalDocs"]!["url"]!.GetValue<string>().Should().Be("https://docs.example.com");
        if (version != OpenApiSpecVersion.OpenApi3_0)
            document["info"]!["summary"]!.GetValue<string>().Should().Be("Target-typed");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Tag_IsRead(OpenApiSpecVersion version)
    {
        var tag = fixture.Builds[version].Document["tags"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "SchemaKeywords")!;

        tag["externalDocs"]!["url"]!.GetValue<string>().Should().Be("https://tags.example.com");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SecuritySchemes_AndRequirements_AreRead(OpenApiSpecVersion version)
    {
        var (document, diagnostics) = fixture.Builds[version];
        var schemes = document["components"]!["securitySchemes"]!;

        schemes["ApiKey"]!.ToJsonString().Should().Be("""{"type":"apiKey","name":"X-Api-Key","in":"header"}""");
        schemes["Bearer"]!.ToJsonString().Should().Be("""{"type":"http","scheme":"bearer"}""");
        schemes["OAuth"]!["flows"]!["clientCredentials"]!.ToJsonString()
            .Should().Be("""{"tokenUrl":"https://auth.example.com/token","scopes":{"read":"Read"}}""");
        document["security"]!.ToJsonString().Should().Be("""[{"ApiKey":[]},{"Bearer":[],"OAuth":["read"]}]""");
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeNotStatic
                                             || d.Code == ExtractionDiagnosticCodes.DocumentMetadataNotStatic
                                             || d.Code == ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScheme);
    }

    // ── Forms that are not read: a warning instead of a silent loss ──────────────

    private static SourceAnalysisContext Context(string body)
    {
        var tree = CSharpSyntaxTree.ParseText($"builder.Services.AddSwaggerGen(c => {{ {body} }});", new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("TestAssembly", [tree], options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        return new SourceAnalysisContext(new SourceCompilationResult("/inline", compilation, [tree]), ((CSharpSyntaxTree)tree).GetCompilationUnitRoot());
    }

    [Theory]
    [InlineData("""c.SwaggerDoc("v1", info);""", "info")]
    [InlineData("""c.SwaggerDoc("v1", new() { Title = "T", License = license });""", "license")]
    [InlineData("""c.SwaggerDoc("v1", new OpenApiInfo { Title = "T", ExternalDocs = Docs() });""", "Docs()")]
    [InlineData("""c.AddTag(tag);""", "tag")]
    [InlineData("""c.AddTag(new() { Name = "T", ExternalDocs = docs });""", "docs")]
    public void UnreadableDocumentMetadata_GivesOneWarning(string body, string expression)
    {
        var diagnostics = new List<ExtractionDiagnostic>();
        DocumentTagsExtractor.Extract(Context(body), diagnostics.Add);

        diagnostics.Should().ContainSingle().Which.Should().Match<ExtractionDiagnostic>(d =>
            d.Code == ExtractionDiagnosticCodes.DocumentMetadataNotStatic && d.Subjects[1] == expression);
    }

    [Theory]
    [InlineData("""c.SwaggerDoc("v1", new() { Title = "T", License = null });""")]
    [InlineData("""c.SwaggerDoc("v1", new() { Title = "T", License = new() { Name = "MIT" } });""")]
    public void ReadableOrAbsentDocumentMetadata_GivesNoWarning(string body)
    {
        var diagnostics = new List<ExtractionDiagnostic>();
        DocumentTagsExtractor.Extract(Context(body), diagnostics.Add);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void SecurityDefinition_ThatIsNotACreation_IsOmittedAsNotStatic()
    {
        var result = SecuritySchemeExtractor.Extract(Context("""c.AddSecurityDefinition("ApiKey", scheme);"""));

        result.Schemes.Should().BeEmpty();
        result.OmittedSchemes.Should().Equal("ApiKey");
    }
}
