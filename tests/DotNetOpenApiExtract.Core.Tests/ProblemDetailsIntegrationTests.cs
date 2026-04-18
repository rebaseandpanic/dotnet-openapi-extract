using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests for the ProblemDetails injection pipeline.
///
/// Strategy (option C from the task spec):
/// <list type="bullet">
///   <item>
///     <see cref="OpenApiDocumentBuilder.ApplyProblemDetails"/> is <c>internal static</c>,
///     accessible via the <c>InternalsVisibleTo</c> attribute in the core project.
///   </item>
///   <item>
///     Tests build a minimal <see cref="OpenApiDocument"/> in memory and call the method
///     directly — no temp-directory compilation needed.
///   </item>
///   <item>
///     SampleApi's <c>Program.cs</c> does NOT call <c>AddProblemDetails()</c> to avoid
///     polluting the existing response-expectation test suite.
///   </item>
/// </list>
/// </summary>
public sealed class ProblemDetailsIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 10. ApplyProblemDetails adds ProblemDetails schema to components
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyProblemDetails_AddsSchemaToComponents()
    {
        var document = BuildDocumentWithOneOperation(existingResponses: null);

        OpenApiDocumentBuilder.ApplyProblemDetails(document);

        document.Components.Should().NotBeNull();
        document.Components!.Schemas.Should().ContainKey(ProblemDetailsSchema.SchemaId,
            because: "ApplyProblemDetails must register the RFC 7807 schema in components");

        var schema = document.Components.Schemas[ProblemDetailsSchema.SchemaId];
        schema.Should().BeOfType<OpenApiSchema>();
        ((OpenApiSchema)schema).Properties.Should().ContainKey("type");
        ((OpenApiSchema)schema).Properties.Should().ContainKey("title");
        ((OpenApiSchema)schema).Properties.Should().ContainKey("status");
        ((OpenApiSchema)schema).Properties.Should().ContainKey("detail");
        ((OpenApiSchema)schema).Properties.Should().ContainKey("instance");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. ApplyProblemDetails injects responses to all operations
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyProblemDetails_InjectsResponsesToOperations()
    {
        var document = BuildDocumentWithOneOperation(existingResponses: null);

        OpenApiDocumentBuilder.ApplyProblemDetails(document);

        var operation = GetSingleOperation(document);
        operation.Responses.Should().ContainKey("400");
        operation.Responses.Should().ContainKey("422");
        operation.Responses.Should().ContainKey("500");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. Existing responses not overwritten
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyProblemDetails_ExistingResponsesNotOverwritten()
    {
        var custom400 = new OpenApiResponse { Description = "Custom validation error schema" };
        var document = BuildDocumentWithOneOperation(existingResponses: new OpenApiResponses
        {
            ["400"] = custom400,
        });

        OpenApiDocumentBuilder.ApplyProblemDetails(document);

        var operation = GetSingleOperation(document);
        operation.Responses!["400"].Should().BeSameAs(custom400,
            because: "a pre-declared 400 response must not be replaced by the default");
        // 422 and 500 should have been injected
        operation.Responses.Should().ContainKey("422");
        operation.Responses.Should().ContainKey("500");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Full-pipeline gate: when detector=false → schema NOT added to components
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithoutAddProblemDetails_SchemaAbsentFromComponents()
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

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // ProblemDetails schema must NOT be present when AddProblemDetails() is not called.
        var hasSchema = document.Components?.Schemas?.ContainsKey(ProblemDetailsSchema.SchemaId) == true;
        hasSchema.Should().BeFalse(
            because: "ProblemDetails schema must only be injected when AddProblemDetails() is detected");
    }

    [Fact]
    public void Build_WithAddProblemDetails_SchemaInComponents()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.AddProblemDetails();
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

        document.Components.Should().NotBeNull();
        document.Components!.Schemas.Should().ContainKey(ProblemDetailsSchema.SchemaId,
            because: "AddProblemDetails() in Program.cs must trigger schema injection");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 13. ProblemDetails schema has AdditionalProperties set (RFC 7807 §3.2 extensions)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyProblemDetails_SchemaHasAdditionalProperties()
    {
        var document = BuildDocumentWithOneOperation(existingResponses: null);

        OpenApiDocumentBuilder.ApplyProblemDetails(document);

        var schema = (OpenApiSchema)document.Components!.Schemas![ProblemDetailsSchema.SchemaId];
        schema.AdditionalProperties.Should().NotBeNull(
            because: "RFC 7807 §3.2 allows extension members — AdditionalProperties must not block them");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 14. Response schema on injected 400 is a $ref to ProblemDetails (integration)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyProblemDetails_InjectedResponseSchemaIsSchemaReference()
    {
        var document = BuildDocumentWithOneOperation(existingResponses: null);

        OpenApiDocumentBuilder.ApplyProblemDetails(document);

        var operation = GetSingleOperation(document);
        var mediaType = operation.Responses!["400"].Content!["application/problem+json"];
        mediaType.Schema.Should().BeOfType<OpenApiSchemaReference>(
            because: "injected response body schema must be a $ref to #/components/schemas/ProblemDetails, not inlined");
        ((OpenApiSchemaReference)mediaType.Schema!).Reference?.Id.Should().Be(ProblemDetailsSchema.SchemaId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal document containing one path item with one GET operation.
    /// </summary>
    private static OpenApiDocument BuildDocumentWithOneOperation(OpenApiResponses? existingResponses)
    {
        var operation = new OpenApiOperation
        {
            Responses = existingResponses ?? new OpenApiResponses(),
        };

        var pathItem = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation>
            {
                [HttpMethod.Get] = operation,
            },
        };

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test", Version = "v1" },
            Paths = new OpenApiPaths { ["/test"] = pathItem },
        };
    }

    private static OpenApiOperation GetSingleOperation(OpenApiDocument document)
    {
        var pathItem = (OpenApiPathItem)document.Paths!["/test"];
        return pathItem.Operations![HttpMethod.Get];
    }
}
