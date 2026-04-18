using AwesomeAssertions;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

public class SourceCompilerTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 6. Compiles SampleApi sources
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceCompiler_CompilesSampleApiSources()
    {
        // Resolve source root from SampleApi.dll
        var dllPath = TestPaths.SampleApiDll;
        var resolved = SourceRootResolver.TryResolve(dllPath, out var sourceRoot, out var reason);
        resolved.Should().BeTrue(because: reason ?? "no reason");

        var result = SourceCompiler.Compile(sourceRoot!);

        result.Should().NotBeNull();
        result.SyntaxTrees.Should().NotBeEmpty();
        result.Compilation.Should().NotBeNull();

        // Program.cs must be among the parsed trees
        var filePaths = result.SyntaxTrees.Select(t => t.FilePath).ToList();
        filePaths.Should().Contain(p =>
            p.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase),
            because: "Program.cs is in the SampleApi source root");

        // UsersController.cs must be found
        filePaths.Should().Contain(p =>
            p.EndsWith("UsersController.cs", StringComparison.OrdinalIgnoreCase),
            because: "UsersController.cs is in SampleApi/Controllers/");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Excludes bin/ and obj/ directories
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceCompiler_ExcludesBinObj()
    {
        using var tmp = new TempDirectory();

        // Create a minimal project structure
        File.WriteAllText(Path.Combine(tmp.Path, "Proj.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(tmp.Path, "MyCode.cs"), "class Foo {}");

        // Plant fake generated files in bin/ and obj/
        var binDir = Path.Combine(tmp.Path, "bin");
        var objDir = Path.Combine(tmp.Path, "obj");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(objDir);

        File.WriteAllText(Path.Combine(binDir, "GeneratedInBin.cs"), "// generated");
        File.WriteAllText(Path.Combine(objDir, "GeneratedInObj.cs"), "// generated");

        var result = SourceCompiler.Compile(tmp.Path);

        var filePaths = result.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();

        filePaths.Should().Contain("MyCode.cs");
        filePaths.Should().NotContain("GeneratedInBin.cs",
            because: "bin/ directory must be excluded");
        filePaths.Should().NotContain("GeneratedInObj.cs",
            because: "obj/ directory must be excluded");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Empty source root (no .cs files) — returns result with zero trees, does not throw
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceCompiler_EmptySourceRoot_ReturnsZeroSyntaxTrees()
    {
        // A directory that exists but contains no .cs files at all.
        // Compile() must not throw and must return an empty SyntaxTrees list
        // so callers get a usable (but empty) SourceCompilationResult.
        using var tmp = new TempDirectory();

        // Put a csproj so the directory looks like a project root, but no .cs files
        File.WriteAllText(Path.Combine(tmp.Path, "Empty.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var act = () => SourceCompiler.Compile(tmp.Path);
        act.Should().NotThrow(because: "an empty project is valid — no sources yet");

        var result = SourceCompiler.Compile(tmp.Path);
        result.Should().NotBeNull();
        result.SyntaxTrees.Should().BeEmpty(because: "no .cs files were present");
        result.Compilation.Should().NotBeNull(because: "Roslyn allows an empty compilation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. .cs file containing syntax errors — compilation is still returned
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceCompiler_SyntaxErrors_StillReturnsSyntaxTrees()
    {
        // Roslyn ParseText never throws — it produces a tree with error nodes.
        // Compile() must not throw and must include the broken file in SyntaxTrees
        // so that valid parts of the project can still be analyzed.
        using var tmp = new TempDirectory();

        File.WriteAllText(Path.Combine(tmp.Path, "Broken.cs"),
            "class Broken { void NotClosed( { }");  // deliberately malformed

        File.WriteAllText(Path.Combine(tmp.Path, "Good.cs"),
            "class Good {}");

        var act = () => SourceCompiler.Compile(tmp.Path);
        act.Should().NotThrow(because: "syntax errors in sources must not prevent compilation");

        var result = SourceCompiler.Compile(tmp.Path);
        var fileNames = result.SyntaxTrees.Select(t => Path.GetFileName(t.FilePath)).ToList();

        fileNames.Should().Contain("Broken.cs",
            because: "broken file is still parsed into a syntax tree (with error nodes)");
        fileNames.Should().Contain("Good.cs",
            because: "valid file alongside a broken file must be included");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. Null or whitespace sourceRoot argument — throws ArgumentException
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SourceCompiler_NullOrWhitespace_ThrowsArgumentException(string? sourceRoot)
    {
        var act = () => SourceCompiler.Compile(sourceRoot!);
        act.Should().Throw<ArgumentException>(
            because: "null/empty/whitespace sourceRoot is a programming error, not a best-effort case");
    }
}
