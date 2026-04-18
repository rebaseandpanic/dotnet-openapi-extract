using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="GlobalMediaTypesExtractor"/>.
/// All tests use inline Roslyn compilation to simulate entry-point source.
/// </summary>
public class GlobalMediaTypesExtractorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Empty context → empty result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ContextUnavailable_ReturnsEmpty()
    {
        var result = GlobalMediaTypesExtractor.Extract(SourceAnalysisContext.Empty);

        result.ProducesContentTypes.Should().BeEmpty(
            because: "an unavailable context has no entry-point source to scan");
        result.ConsumesContentTypes.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. AddControllers with ProducesAttribute
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddControllers_ProducesAttribute_ReturnsContentType()
    {
        var context = BuildContext("""
            builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute("application/json")));
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ProducesContentTypes.Should().Equal(["application/json"]);
        result.ConsumesContentTypes.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. AddControllers with ConsumesAttribute
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddControllers_ConsumesAttribute_ReturnsContentType()
    {
        var context = BuildContext("""
            builder.Services.AddControllers(o => o.Filters.Add(new ConsumesAttribute("application/json")));
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ConsumesContentTypes.Should().Equal(["application/json"]);
        result.ProducesContentTypes.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. AddMvc with ProducesAttribute
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddMvc_ProducesAttribute_ReturnsContentType()
    {
        var context = BuildContext("""
            builder.Services.AddMvc(o => o.Filters.Add(new ProducesAttribute("application/json")));
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ProducesContentTypes.Should().Equal(["application/json"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Multi-argument constructor: new ProducesAttribute("application/json", "text/xml")
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_MultipleContentTypes_AllCollected()
    {
        var context = BuildContext("""
            builder.Services.AddControllers(o =>
                o.Filters.Add(new ProducesAttribute("application/json", "text/xml")));
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ProducesContentTypes.Should().Equal(["application/json", "text/xml"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Multiple Filters.Add calls with different content types
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_MultipleFilterAdds_AllCollected()
    {
        var context = BuildContext("""
            builder.Services.AddControllers(o =>
            {
                o.Filters.Add(new ProducesAttribute("application/json"));
                o.Filters.Add(new ProducesAttribute("text/xml"));
            });
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ProducesContentTypes.Should().Equal(["application/json", "text/xml"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Non-literal content type → skipped (with warning)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NonLiteralContentType_Skipped()
    {
        var context = BuildContext("""
            var ct = "application/json";
            builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute(ct)));
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ProducesContentTypes.Should().BeEmpty(
            because: "non-literal arguments cannot be statically resolved and must be skipped");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. No AddControllers/AddMvc call → empty result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NoCall_ReturnsEmpty()
    {
        var context = BuildContext("""
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ProducesContentTypes.Should().BeEmpty();
        result.ConsumesContentTypes.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Fully-qualified attribute name is resolved to the short name correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ShortAttributeName_Produces_Works()
    {
        // "Produces" without the "Attribute" suffix — also valid C# syntax
        var context = BuildContext("""
            builder.Services.AddControllers(o => o.Filters.Add(new Produces("application/xml")));
            """);

        var result = GlobalMediaTypesExtractor.Extract(context);

        result.ProducesContentTypes.Should().Equal(["application/xml"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> from inline top-level statement source.
    /// </summary>
    private static SourceAnalysisContext BuildContext(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult(string.Empty, compilation, [tree]);
        var root = tree.GetCompilationUnitRoot();

        return new SourceAnalysisContext(compilationResult, root);
    }
}
