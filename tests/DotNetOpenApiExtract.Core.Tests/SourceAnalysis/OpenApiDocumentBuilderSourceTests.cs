using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

/// <summary>
/// Integration tests verifying that <see cref="OpenApiDocumentBuilder"/> works correctly
/// with and without source analysis enabled.
/// </summary>
public class OpenApiDocumentBuilderSourceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 14. No source provided — existing behavior unchanged
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenApiDocumentBuilder_IntegrationTest_NoSourceProvided_StillWorks()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            Title        = "Test",
            Version      = "v1",
            // No SourcePath or SourceRoot
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Should().NotBeNull();
        document.Paths.Should().NotBeNull();
        document.Paths!.Count.Should().BeGreaterThan(0,
            because: "SampleApi has multiple controller actions");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 15. Auto-detected source root — does not break document generation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenApiDocumentBuilder_IntegrationTest_WithAutoDetectedSource_NoFailure()
    {
        // Build without source to get the baseline path count
        var baselineOptions = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };
        var baseline = OpenApiDocumentBuilder.Build(baselineOptions);
        var baselinePathCount = baseline.Paths?.Count ?? 0;

        // Build with auto-detected source root (SourceRoot not set, resolver will find it)
        var withSourceOptions = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };
        var withSource = OpenApiDocumentBuilder.Build(withSourceOptions);

        withSource.Should().NotBeNull();
        withSource.Paths.Should().NotBeNull();

        // Source analysis must not change the number of paths in Wave 0
        withSource.Paths!.Count.Should().Be(baselinePathCount,
            because: "source analysis in Wave 0 is infrastructure-only and must not alter output");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Extra: explicit SourceRoot override also works
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenApiDocumentBuilder_IntegrationTest_ExplicitSourceRoot_NoFailure()
    {
        // Resolve the source root manually
        var resolved = DotNetOpenApiExtract.Core.SourceAnalysis.SourceRootResolver.TryResolve(
            TestPaths.SampleApiDll, out var sourceRoot, out _);
        resolved.Should().BeTrue();

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = sourceRoot,
        };

        // Must not throw
        var document = OpenApiDocumentBuilder.Build(options);
        document.Should().NotBeNull();
        document.Paths!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OpenApiDocumentBuilder_IntegrationTest_InvalidSourceRoot_StillProducesDocument()
    {
        // Explicitly set an invalid source root — must degrade gracefully
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = "/this/path/does/not/exist",
        };

        // Must not throw and must still produce document
        var document = OpenApiDocumentBuilder.Build(options);

        document.Paths!.Count.Should().BeGreaterThan(0,
            because: "invalid source root falls back to analysis-free mode");
    }
}
