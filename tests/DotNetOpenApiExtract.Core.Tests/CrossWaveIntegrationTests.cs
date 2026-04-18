using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Cross-wave integration tests that verify the ordering guarantees between
/// Step 8 (ProblemDetails injection) and Step 9 (GlobalResponseHeaders application).
/// </summary>
/// <remarks>
/// Step 8 injects 400/422/500 responses into all operations when
/// <c>AddProblemDetails()</c> is detected in Program.cs.
/// Step 9 applies middleware-detected response headers to ALL responses in the document,
/// including those injected in Step 8.
/// These tests confirm that injected ProblemDetails responses receive global middleware headers.
/// </remarks>
public sealed class CrossWaveIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // I2. ProblemDetails-injected responses must receive global middleware headers
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ProblemDetailsAndGlobalHeaders_HeadersAppearOnInjectedResponses()
    {
        // Arrange: Program.cs with both AddProblemDetails() and middleware that appends
        // a response header. Step 8 runs before Step 9, so the PD-injected responses
        // must be present when ApplyGlobalResponseHeaders iterates the document.
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddProblemDetails();
            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append("X-Request-Id", "test-id");
                await next();
            });

            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        // Act
        var document = OpenApiDocumentBuilder.Build(options);

        // Assert 1: ProblemDetails schema was injected into components.
        document.Components.Should().NotBeNull(
            because: "AddProblemDetails() must trigger ProblemDetails schema injection");
        document.Components!.Schemas.Should().ContainKey(ProblemDetailsSchema.SchemaId,
            because: "Step 8 must register the RFC 7807 schema in components");

        // Assert 2: every operation has an injected 400 response (from Step 8)
        // AND that response carries the X-Request-Id header (from Step 9).
        document.Paths.Should().NotBeNull();
        document.Paths.Should().NotBeEmpty();

        var allOperations = document.Paths!
            .Values
            .OfType<OpenApiPathItem>()
            .SelectMany(p => p.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            .ToList();

        allOperations.Should().NotBeEmpty(
            because: "SampleApi must expose at least one operation");

        foreach (var operation in allOperations)
        {
            // ProblemDetails injection (Step 8) must have added 400.
            operation.Responses.Should().ContainKey("400",
                because: "AddProblemDetails() must inject a 400 default response into every operation");

            var response400 = operation.Responses["400"] as OpenApiResponse;
            response400.Should().NotBeNull();

            // Global middleware headers (Step 9) must have been applied AFTER Step 8,
            // so PD-injected responses must carry the header too.
            response400!.Headers.Should().NotBeNull(
                because: "Step 9 (ApplyGlobalResponseHeaders) runs after Step 8 (ProblemDetails injection)");
            response400.Headers.Should().ContainKey("X-Request-Id",
                because: "global middleware header must be applied to ProblemDetails-injected responses " +
                         "because Step 9 runs after Step 8");
        }
    }
}
