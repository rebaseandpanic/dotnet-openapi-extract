using AwesomeAssertions;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

public class SourceRootResolverTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Standard layout — DLL inside SampleApi/bin/Debug/net10.0/
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceRootResolver_StandardLayout_Found()
    {
        var dllPath = TestPaths.SampleApiDll;

        var found = SourceRootResolver.TryResolve(dllPath, out var sourceRoot, out var failureReason);

        found.Should().BeTrue(because: failureReason ?? "no reason");
        sourceRoot.Should().NotBeNullOrEmpty();

        // The resolved root must contain SampleApi.csproj
        var csproj = Path.Combine(sourceRoot!, "SampleApi.csproj");
        File.Exists(csproj).Should().BeTrue(
            because: $"SampleApi.csproj should exist at {csproj}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. <AssemblyName> override in csproj
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceRootResolver_AssemblyNameOverride()
    {
        using var tmp = new TempDirectory();

        // Foo/ contains Foo.csproj with <AssemblyName>Bar</AssemblyName>
        var projDir = Path.Combine(tmp.Path, "Foo");
        Directory.CreateDirectory(projDir);

        File.WriteAllText(Path.Combine(projDir, "Foo.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>Bar</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        // Simulate Bar.dll in a non-standard output path so the resolver walks up into Foo/
        var outDir = Path.Combine(projDir, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "Bar.dll"), []);

        var found = SourceRootResolver.TryResolve(
            Path.Combine(outDir, "Bar.dll"),
            out var sourceRoot,
            out var failureReason);

        found.Should().BeTrue(because: failureReason ?? "no reason");
        sourceRoot.Should().Be(projDir);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Multiple csproj files in one folder — match by filename
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceRootResolver_MultipleProjectsAmbiguous_MatchByName()
    {
        using var tmp = new TempDirectory();

        // Two .csproj files in the same directory
        File.WriteAllText(Path.Combine(tmp.Path, "Foo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(tmp.Path, "Bar.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var outDir = Path.Combine(tmp.Path, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, "Foo.dll"), []);

        var found = SourceRootResolver.TryResolve(
            Path.Combine(outDir, "Foo.dll"),
            out var sourceRoot,
            out var failureReason);

        found.Should().BeTrue(because: failureReason ?? "no reason");
        // The resolved root is the directory that contains both .csproj files
        sourceRoot.Should().Be(tmp.Path);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Multiple mismatched csproj files — returns false, does not throw
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceRootResolver_NoProjectFound_ReturnsFalse()
    {
        // Per spec: when multiple .csproj files exist but none match the assembly name
        // (by filename or <AssemblyName>), the resolver returns false.
        // We cannot use a single-csproj scenario because single csproj is always selected
        // regardless of name (per spec: "one .csproj — take it").
        using var tmp = new TempDirectory();

        // Two .csproj files that do NOT match "Lonely" (no <AssemblyName> override)
        File.WriteAllText(Path.Combine(tmp.Path, "Alpha.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(tmp.Path, "Beta.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var outDir = Path.Combine(tmp.Path, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(outDir);
        var dllPath = Path.Combine(outDir, "Lonely.dll");
        File.WriteAllBytes(dllPath, []);

        // Must not throw
        var act = () => SourceRootResolver.TryResolve(dllPath, out _, out _);
        act.Should().NotThrow();

        // The resolver must return false: two mismatched csproj files, no match for "Lonely"
        var found = SourceRootResolver.TryResolve(dllPath, out var sourceRoot, out var reason);
        found.Should().BeFalse(because: reason ?? "no csproj matches assembly name 'Lonely'");
        sourceRoot.Should().BeNull();
        reason.Should().NotBeNullOrEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Null or empty assemblyPath — does not throw, returns false
    // ──────────────────────────────────────────────────────────────────────────
    // TryResolve is documented to "never throw". Passing null or "" causes
    // Path.GetFullPath to throw ArgumentNullException/ArgumentException inside
    // the try/catch, which is silently caught and returned as false.
    // This test verifies the contract is upheld.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SourceRootResolver_NullOrEmptyPath_DoesNotThrow_ReturnsFalse(string? path)
    {
        // Must not throw (exception is caught by the internal try/catch)
        var act = () => SourceRootResolver.TryResolve(path!, out _, out _);
        act.Should().NotThrow(because: "TryResolve is documented to never throw");

        var found = SourceRootResolver.TryResolve(path!, out var sourceRoot, out _);
        found.Should().BeFalse();
        sourceRoot.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Custom output path (not inside bin/Debug/...)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceRootResolver_CustomOutputPath()
    {
        using var tmp = new TempDirectory();

        var projDir = Path.Combine(tmp.Path, "X");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "Proj.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        // DLL is in a completely custom output path, not bin/Debug/...
        var customOut = Path.Combine(projDir, "custom-out");
        Directory.CreateDirectory(customOut);
        File.WriteAllBytes(Path.Combine(customOut, "Proj.dll"), []);

        var found = SourceRootResolver.TryResolve(
            Path.Combine(customOut, "Proj.dll"),
            out var sourceRoot,
            out var failureReason);

        found.Should().BeTrue(because: failureReason ?? "no reason");
        sourceRoot.Should().Be(projDir);
    }
}
