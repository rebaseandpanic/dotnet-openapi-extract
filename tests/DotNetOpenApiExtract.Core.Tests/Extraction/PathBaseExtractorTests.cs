using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="PathBaseExtractor"/> and the
/// <see cref="OpenApiDocumentBuilder.ApplyPathBase"/> helpers.
/// </summary>
public class PathBaseExtractorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Empty context → null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_ContextUnavailable_ReturnsNull()
    {
        var result = PathBaseExtractor.ExtractPathBase(SourceAnalysisContext.Empty);

        result.Should().BeNull(because: "an unavailable context has no entry-point source to scan");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Literal with leading slash → returned as-is
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_LiteralLeadingSlash_Returned()
    {
        var context = BuildContext("""app.UsePathBase("/api/v1");""");

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().Be("/api/v1");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Literal without leading slash → normalised with /
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_LiteralNoLeadingSlash_NormalizedWithSlash()
    {
        var context = BuildContext("""app.UsePathBase("api");""");

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().Be("/api",
            because: "a missing leading slash must be added during normalisation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Trailing slash → trimmed
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_LiteralTrailingSlash_Trimmed()
    {
        var context = BuildContext("""app.UsePathBase("/api/v1/");""");

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().Be("/api/v1",
            because: "trailing slashes are removed during normalisation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Empty string → null (no-op path base)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_EmptyString_ReturnsNull()
    {
        var context = BuildContext("""app.UsePathBase("");""");

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().BeNull(because: "UsePathBase(\"\") is a no-op and must be treated as absent");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Just slash → null (no-op path base)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_JustSlash_ReturnsNull()
    {
        var context = BuildContext("""app.UsePathBase("/");""");

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().BeNull(because: "UsePathBase(\"/\") is a no-op and must be treated as absent");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Non-literal argument → null + warning
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_NonLiteralArgument_ReturnsNull()
    {
        var context = BuildContext("""app.UsePathBase(config["path"]);""");

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().BeNull(
            because: "a non-literal argument cannot be statically resolved");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. No UsePathBase invocation → null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_NoInvocation_ReturnsNull()
    {
        var context = BuildContext("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().BeNull(because: "no UsePathBase call is present in the source");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Multiple UsePathBase invocations → first returned, warning emitted
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPathBase_MultipleInvocations_FirstReturned()
    {
        var context = BuildContext("""
            app.UsePathBase("/api/v1");
            app.UsePathBase("/api/v2");
            """);

        var result = PathBaseExtractor.ExtractPathBase(context);

        result.Should().Be("/api/v1",
            because: "when multiple UsePathBase calls exist, the first one is used");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NormalizePathBase — direct unit tests
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("",       null)]
    [InlineData("/",      null)]
    [InlineData("//",     null)]
    [InlineData("api",    "/api")]
    [InlineData("/api",   "/api")]
    [InlineData("/api/",  "/api")]
    [InlineData("/api/v1/", "/api/v1")]
    public void NormalizePathBase_VariousInputs_CorrectOutput(string input, string? expected)
    {
        var result = PathBaseExtractor.NormalizePathBase(input);

        result.Should().Be(expected);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> from inline top-level statement source.
    /// Uses a <see cref="CompilationUnitSyntax"/> as the entry-point node, which is the
    /// same shape as a real Program.cs compiled with top-level statements.
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
