using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests for the global response-headers pipeline:
/// <see cref="ResponseHeaderExtractor"/> + <see cref="OpenApiDocumentBuilder.ApplyGlobalResponseHeaders"/>.
/// </summary>
public sealed class ResponseHeadersIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 9. Full pipeline: middleware headers applied to all responses
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithMiddlewareHeaders_AppliedToAllResponses()
    {
        using var tempDir = new TempDirectory();

        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("X-Request-Id", Guid.NewGuid().ToString());
                await next();
            });
            app.MapControllers();
            app.Run();
            """);

        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // Every operation's every response must carry the X-Request-Id header.
        document.Paths.Should().NotBeNull();
        document.Paths.Should().NotBeEmpty();

        foreach (var (path, pathItemInterface) in document.Paths!)
        {
            var pathItem = pathItemInterface as OpenApiPathItem;
            if (pathItem?.Operations == null)
                continue;

            foreach (var (method, operation) in pathItem.Operations)
            {
                if (operation.Responses == null)
                    continue;

                foreach (var (statusCode, responseInterface) in operation.Responses)
                {
                    var response = responseInterface as OpenApiResponse;
                    response.Should().NotBeNull(
                        because: $"response for {method} {path} [{statusCode}] should be OpenApiResponse");

                    response!.Headers.Should().NotBeNull(
                        because: $"{method} {path} [{statusCode}] should have headers injected by middleware");
                    response.Headers.Should().ContainKey("X-Request-Id",
                        because: $"X-Request-Id must be present on {method} {path} [{statusCode}]");
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. No middleware headers → no extra headers added
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithoutMiddlewareHeaders_NoHeadersAdded()
    {
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

        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // No middleware → responses should not have any globally-injected headers.
        // (They may still have headers from other sources, but the count from
        //  ApplyGlobalResponseHeaders must be zero since we inject nothing.)
        document.Paths.Should().NotBeNull();

        foreach (var (_, pathItemInterface) in document.Paths!)
        {
            var pathItem = pathItemInterface as OpenApiPathItem;
            if (pathItem?.Operations == null)
                continue;

            foreach (var (_, operation) in pathItem.Operations)
            {
                if (operation.Responses == null)
                    continue;

                foreach (var (_, responseInterface) in operation.Responses)
                {
                    var response = responseInterface as OpenApiResponse;
                    if (response == null)
                        continue;

                    // Headers might be null or empty — either is fine.
                    // The key assertion is that no X-Request-Id was injected.
                    var hasInjected = response.Headers?.ContainsKey("X-Request-Id") == true;
                    hasInjected.Should().BeFalse(
                        because: "X-Request-Id must not be injected when no middleware sets it");
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. ApplyGlobalResponseHeaders: existing header not overwritten
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyGlobalResponseHeaders_ExistingHeader_NotOverwritten()
    {
        // Build a minimal document with a pre-existing header on a response.
        var existingHeader = new OpenApiHeader
        {
            Description = "Pre-existing custom header",
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        var response = new OpenApiResponse
        {
            Description = "OK",
            Headers = new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Request-Id"] = existingHeader,
            },
        };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses { ["200"] = response },
        };

        var pathItem = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = operation,
            },
        };

        var document = new OpenApiDocument
        {
            Info  = new OpenApiInfo { Title = "Test", Version = "v1" },
            Paths = new OpenApiPaths { ["/test"] = pathItem },
        };

        // Inject: X-Request-Id already present — must not be overwritten.
        OpenApiDocumentBuilder.ApplyGlobalResponseHeaders(document, ["X-Request-Id", "X-Trace-Id"]);

        // The pre-existing header must be intact (same reference).
        response.Headers.Should().ContainKey("X-Request-Id");
        response.Headers!["X-Request-Id"].Should().BeSameAs(existingHeader,
            because: "ApplyGlobalResponseHeaders must not overwrite an existing header");

        // The new header must have been added.
        response.Headers.Should().ContainKey("X-Trace-Id",
            because: "X-Trace-Id was not present before and should be injected");
    }
}
