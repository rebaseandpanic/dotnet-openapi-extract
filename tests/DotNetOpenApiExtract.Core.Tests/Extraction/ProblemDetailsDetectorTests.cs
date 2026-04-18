using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="ProblemDetailsDetector"/>.
/// </summary>
public sealed class ProblemDetailsDetectorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Context unavailable → false
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRegistered_ContextUnavailable_False()
    {
        var result = ProblemDetailsDetector.IsRegistered(SourceAnalysisContext.Empty);

        result.Should().BeFalse(because: "unavailable context has no entry point to analyse");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. AddProblemDetails() call present → true
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRegistered_AddProblemDetailsPresent_True()
    {
        var context = BuildContext("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddProblemDetails();
            var app = builder.Build();
            app.Run();
            """);

        var result = ProblemDetailsDetector.IsRegistered(context);

        result.Should().BeTrue(because: "AddProblemDetails() is present in the entry point");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. AddProblemDetails(options => { ... }) with lambda → true
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRegistered_AddProblemDetailsWithOptions_True()
    {
        var context = BuildContext("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddProblemDetails(options => {
                options.CustomizeProblemDetails = ctx => { };
            });
            var app = builder.Build();
            app.Run();
            """);

        var result = ProblemDetailsDetector.IsRegistered(context);

        result.Should().BeTrue(because: "AddProblemDetails with a configure lambda still counts");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. AddProblemDetails absent → false
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsRegistered_AddProblemDetailsAbsent_False()
    {
        var context = BuildContext("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.Run();
            """);

        var result = ProblemDetailsDetector.IsRegistered(context);

        result.Should().BeFalse(because: "AddProblemDetails() is not called in this entry point");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> from top-level statement source code.
    /// </summary>
    private static SourceAnalysisContext BuildContext(string topLevelStatements)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var ct = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(topLevelStatements, parseOptions, cancellationToken: ct);

        var compilation = CSharpCompilation.Create(
            "TestDetectorAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult("/test", compilation, [tree]);

        // Top-level statements produce a CompilationUnitSyntax as the entry-point node.
        var entryPointNode = tree.GetCompilationUnitRoot(ct);

        return new SourceAnalysisContext(compilationResult, entryPointNode);
    }
}
