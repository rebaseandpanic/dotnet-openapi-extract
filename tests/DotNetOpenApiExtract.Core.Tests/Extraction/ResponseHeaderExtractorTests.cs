using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="ResponseHeaderExtractor"/>.
/// </summary>
public class ResponseHeaderExtractorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Empty context → empty result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ContextUnavailable_ReturnsEmpty()
    {
        var result = ResponseHeaderExtractor.Extract(SourceAnalysisContext.Empty);

        result.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. app.Use inline lambda with Headers.Append
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AppUseInlineLambda_HeadersAppend_ExtractsName()
    {
        var source = """
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("X-Request-Id", Guid.NewGuid().ToString());
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().ContainSingle().Which.Should().Be("X-Request-Id");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. app.Use inline lambda with Headers.Add
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AppUseInlineLambda_HeadersAdd_ExtractsName()
    {
        var source = """
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add("X-Correlation-Id", "value");
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().ContainSingle().Which.Should().Be("X-Correlation-Id");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. app.UseMiddleware<T> — finds headers in class InvokeAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AppUseMiddleware_FindsInClassInvokeAsync()
    {
        // Top-level statements + class declaration in the same compilation unit
        var source = """
            app.UseMiddleware<CorrelationMiddleware>();

            public class CorrelationMiddleware
            {
                private readonly RequestDelegate _next;
                public CorrelationMiddleware(RequestDelegate next) { _next = next; }

                public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
                {
                    ctx.Response.Headers.Append("X-Correlation", "test-value");
                    await next(ctx);
                }
            }
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().ContainSingle().Which.Should().Be("X-Correlation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. No middleware → empty
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NoMiddleware_ReturnsEmpty()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Non-literal header name → skipped
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NonLiteralName_Skipped()
    {
        var source = """
            var headerName = "X-Custom";
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append(headerName, "value");
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().BeEmpty(
            because: "non-literal header names cannot be resolved statically and must be skipped");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Multiple headers in the same middleware — all extracted
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_MultipleHeadersInSameMiddleware_AllExtracted()
    {
        var source = """
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("X-Request-Id", Guid.NewGuid().ToString());
                context.Response.Headers.Append("X-Trace-Id", "trace");
                context.Response.Headers.Add("Cache-Control", "no-cache");
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().HaveCount(3);
        result.Should().Contain("X-Request-Id");
        result.Should().Contain("X-Trace-Id");
        result.Should().Contain("Cache-Control");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Duplicate header names → deduplicated
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_DuplicateHeaderNames_Deduped()
    {
        var source = """
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("X-Request-Id", "first");
                await next();
            });
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("X-Request-Id", "second");
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().ContainSingle().Which.Should().Be("X-Request-Id",
            because: "duplicate header names from multiple middlewares must appear only once");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Response.Headers indexer assignment — 5.1.3
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_HeadersIndexerAssignment_ExtractsHeaderName()
    {
        var source = """
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Request-Id"] = Guid.NewGuid().ToString();
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().ContainSingle().Which.Should().Be("X-Request-Id",
            because: "indexer assignment 'Headers[name] = value' is a common production pattern");
    }

    [Fact]
    public void Extract_HeadersIndexerRead_NotExtracted()
    {
        // Reading from the indexer (not an assignment) should NOT produce a header.
        var source = """
            app.Use(async (context, next) =>
            {
                var val = context.Response.Headers["X-Foo"];
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().BeEmpty(
            because: "indexer reads are not header mutations");
    }

    [Fact]
    public void Extract_HeadersAppendAndIndexer_BothExtracted()
    {
        // Mix of .Append() and indexer assignment in the same middleware.
        var source = """
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("X-Correlation-Id", "trace");
                context.Response.Headers["X-Request-Id"] = Guid.NewGuid().ToString();
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().HaveCount(2);
        result.Should().Contain("X-Correlation-Id");
        result.Should().Contain("X-Request-Id");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. Non-literal indexer key → skipped (Pattern B warning branch)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_HeadersIndexerAssignment_NonLiteralKey_Skipped()
    {
        // Response.Headers[variable] = value — the indexer key is not a string literal.
        // Pattern B must skip it gracefully (warning to stderr) and return empty.
        // This exercises the else-branch of Pattern B in CollectHeaderNames that was
        // previously unreachable from tests (Pattern A's else-branch was covered via
        // Extract_NonLiteralName_Skipped, but Pattern B's was not).
        var source = """
            app.Use(async (context, next) =>
            {
                var headerName = GetHeaderName();
                context.Response.Headers[headerName] = "value";
                await next();
            });
            """;

        var context = BuildContext(source);
        var result = ResponseHeaderExtractor.Extract(context);

        result.Should().BeEmpty(
            because: "indexer assignment with non-literal header name cannot be resolved statically and must be skipped");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> from an inline source string using
    /// a top-level statement compilation, matching the same pattern used by
    /// <see cref="SecuritySchemeExtractorTests"/>.
    /// </summary>
    private static SourceAnalysisContext BuildContext(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult("/inline", compilation, [tree]);
        var root = ((CSharpSyntaxTree)tree).GetCompilationUnitRoot();

        return new SourceAnalysisContext(compilationResult, root);
    }
}
