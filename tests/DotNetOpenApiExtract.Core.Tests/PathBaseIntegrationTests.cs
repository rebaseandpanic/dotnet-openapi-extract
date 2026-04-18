using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests for path-base extraction and emission.
/// Tests cover <see cref="OpenApiDocumentBuilder.ApplyPathBase"/> directly
/// (Variant C — hand-built <see cref="OpenApiDocument"/>), as well as
/// the full <see cref="OpenApiDocumentBuilder.Build"/> pipeline when a
/// source root with <c>UsePathBase</c> is detected.
/// </summary>
public class PathBaseIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 10. PathPrefix mode — all paths get prefixed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PathPrefix_AllPathsPrefixed()
    {
        var document = BuildDocumentWithPaths("/users", "/orders/{id}", "/health");

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.PathPrefix);

        document.Paths!.Keys.Should().BeEquivalentTo(
            ["/api/v1/users", "/api/v1/orders/{id}", "/api/v1/health"],
            because: "PathPrefix mode prepends the path base to every path key");
    }

    [Fact]
    public void PathPrefix_PathItemsPreserved()
    {
        // Verify the path items (operations etc.) are transferred, not lost.
        var document = BuildDocumentWithPaths("/users");

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.PathPrefix);

        document.Paths!.Should().ContainKey("/api/v1/users");
        document.Paths["/api/v1/users"].Should().NotBeNull();
    }

    [Fact]
    public void PathPrefix_OriginalPathsAbsent()
    {
        var document = BuildDocumentWithPaths("/users", "/orders");

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.PathPrefix);

        document.Paths!.Keys.Should().NotContain("/users",
            because: "original un-prefixed paths must be replaced");
        document.Paths!.Keys.Should().NotContain("/orders");
    }

    [Fact]
    public void PathPrefix_NoExistingServers_ServersUnchanged()
    {
        var document = BuildDocumentWithPaths("/users");
        document.Servers = null;

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.PathPrefix);

        document.Servers.Should().BeNull(
            because: "PathPrefix mode must not touch the servers array");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. ServersEntry mode — server added, paths unchanged
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ServersEntry_AddedToServers()
    {
        var document = BuildDocumentWithPaths("/users", "/orders");

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.ServersEntry);

        document.Servers.Should().NotBeNull();
        document.Servers!.Should().Contain(s => s.Url == "/api/v1",
            because: "ServersEntry mode adds a relative server entry for the path base");
    }

    [Fact]
    public void ServersEntry_PathsUnchanged()
    {
        var document = BuildDocumentWithPaths("/users", "/orders");

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.ServersEntry);

        document.Paths!.Keys.Should().BeEquivalentTo(
            ["/users", "/orders"],
            because: "ServersEntry mode must not modify path keys");
    }

    [Fact]
    public void ServersEntry_ExistingServersPreserved()
    {
        var document = BuildDocumentWithPaths("/users");
        document.Servers = new List<OpenApiServer> { new() { Url = "https://example.com" } };

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.ServersEntry);

        document.Servers.Should().HaveCount(2,
            because: "existing server entries must be preserved when adding the path base entry");
        document.Servers.Should().Contain(s => s.Url == "https://example.com");
        document.Servers.Should().Contain(s => s.Url == "/api/v1");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. No path base → document unchanged
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoPathBase_PathsUnchanged()
    {
        // Simulate extraction yielding null — ApplyPathBase should not be called.
        // Test the ExtractPathBase → no-op path directly.
        var context = BuildContext("""app.MapControllers();""");

        var pathBase = PathBaseExtractor.ExtractPathBase(context);
        pathBase.Should().BeNull();

        // No mutation should occur.
        var document = BuildDocumentWithPaths("/users");
        // Don't call ApplyPathBase when pathBase is null (mirrors the builder logic).
        document.Paths!.Keys.Should().BeEquivalentTo(["/users"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Server dedup — duplicate server not added twice
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ServersEntry_DuplicateNotAdded()
    {
        var document = BuildDocumentWithPaths("/users");
        document.Servers = new List<OpenApiServer> { new() { Url = "/api/v1" } };

        OpenApiDocumentBuilder.ApplyPathBase(document, "/api/v1", PathBaseEmission.ServersEntry);

        document.Servers.Should().HaveCount(1,
            because: "a server entry with the same URL must not be duplicated");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Full-pipeline integration — using SampleApi + modified source root
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithUsePathBase_PathsArePrefixed()
    {
        // Write a temporary Program.cs that contains UsePathBase("/api/test")
        // into a temp directory, then build the document using SampleApi DLL
        // but pointing SourceRoot at the temp dir.
        // This verifies the full Build → extract → apply pipeline end-to-end.
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.UsePathBase("/api/test");
            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath     = TestPaths.SampleApiDll,
            XmlPath          = TestPaths.SampleApiXml,
            SourceRoot       = tempDir.Path,
            PathBaseEmission = PathBaseEmission.PathPrefix,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Paths.Should().NotBeNull();
        document.Paths!.Keys.Should().AllSatisfy(key =>
            key.Should().StartWith("/api/test",
                because: "PathPrefix mode must prepend the path base to every path"));
    }

    [Fact]
    public void Build_WithUsePathBase_ServersEntryEmission()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.UsePathBase("/api/test");
            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath     = TestPaths.SampleApiDll,
            XmlPath          = TestPaths.SampleApiXml,
            SourceRoot       = tempDir.Path,
            PathBaseEmission = PathBaseEmission.ServersEntry,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Servers.Should().NotBeNull();
        document.Servers!.Should().Contain(s => s.Url == "/api/test",
            because: "ServersEntry mode adds path base as a relative servers[] entry");

        // Paths should remain without prefix in ServersEntry mode.
        document.Paths!.Keys.Should().NotContain(
            key => key.StartsWith("/api/test"),
            because: "ServersEntry mode must not modify path keys");
    }

    [Fact]
    public void Build_WithoutUsePathBase_NoPrefixApplied()
    {
        // Control: no UsePathBase → baseline document unchanged.
        var baselineOptions = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };
        var baseline = OpenApiDocumentBuilder.Build(baselineOptions);
        var baselinePaths = baseline.Paths!.Keys.ToHashSet();

        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Paths!.Keys.Should().BeEquivalentTo(baselinePaths,
            because: "no UsePathBase → paths must be identical to the baseline");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal <see cref="OpenApiDocument"/> with the given paths,
    /// each mapped to an empty <see cref="OpenApiPathItem"/>.
    /// </summary>
    private static OpenApiDocument BuildDocumentWithPaths(params string[] paths)
    {
        var document = new OpenApiDocument
        {
            Info  = new OpenApiInfo { Title = "Test", Version = "v1" },
            Paths = new OpenApiPaths(),
        };

        foreach (var path in paths)
            document.Paths[path] = new OpenApiPathItem();

        return document;
    }

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> from inline top-level statement source.
    /// </summary>
    private static SourceAnalysisContext BuildContext(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult(string.Empty, compilation, [tree]);
        var root = tree.GetCompilationUnitRoot();

        return new SourceAnalysisContext(compilationResult, root);
    }
}
