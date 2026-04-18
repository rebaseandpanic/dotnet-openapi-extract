using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Extraction;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="ProblemDetailsResponseInjector"/>.
/// </summary>
public sealed class ProblemDetailsResponseInjectorTests
{
    /// <summary>Schema reference passed to the injector — content mirrors real usage.</summary>
    private static readonly OpenApiSchemaReference SchemaRef =
        new("ProblemDetails", null);

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Operation with no responses → all three defaults added
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_OperationWithoutResponses_AddsAll3Defaults()
    {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses() };

        ProblemDetailsResponseInjector.Inject(operation, SchemaRef);

        operation.Responses.Should().ContainKey("400");
        operation.Responses.Should().ContainKey("422");
        operation.Responses.Should().ContainKey("500");
        operation.Responses.Count.Should().Be(3);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Operation with existing 400 → 400 skipped, 422 and 500 added
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_OperationWith400Existing_Skips400_AddsOthers()
    {
        var existing400 = new OpenApiResponse { Description = "Custom Bad Request" };
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses { ["400"] = existing400 },
        };

        ProblemDetailsResponseInjector.Inject(operation, SchemaRef);

        operation.Responses["400"].Should().BeSameAs(existing400,
            because: "existing 400 must not be overwritten");
        operation.Responses.Should().ContainKey("422");
        operation.Responses.Should().ContainKey("500");
        operation.Responses.Count.Should().Be(3);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Operation with all three already present → no change
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_OperationAllExisting_NoChange()
    {
        var r400 = new OpenApiResponse { Description = "400 existing" };
        var r422 = new OpenApiResponse { Description = "422 existing" };
        var r500 = new OpenApiResponse { Description = "500 existing" };

        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["400"] = r400,
                ["422"] = r422,
                ["500"] = r500,
            },
        };

        ProblemDetailsResponseInjector.Inject(operation, SchemaRef);

        operation.Responses["400"].Should().BeSameAs(r400);
        operation.Responses["422"].Should().BeSameAs(r422);
        operation.Responses["500"].Should().BeSameAs(r500);
        operation.Responses.Count.Should().Be(3);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Added responses use application/problem+json content type
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_ContentTypeIsProblemJson()
    {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses() };

        ProblemDetailsResponseInjector.Inject(operation, SchemaRef);

        foreach (var statusCode in ProblemDetailsResponseInjector.DefaultStatusCodes)
        {
            var response = operation.Responses[statusCode.ToString()];
            response.Content.Should().ContainKey("application/problem+json",
                because: $"response {statusCode} must use application/problem+json");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Schema on added responses is a $ref, not inlined
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Inject_SchemaIsReference()
    {
        var operation = new OpenApiOperation { Responses = new OpenApiResponses() };

        ProblemDetailsResponseInjector.Inject(operation, SchemaRef);

        foreach (var statusCode in ProblemDetailsResponseInjector.DefaultStatusCodes)
        {
            var response = operation.Responses[statusCode.ToString()];
            var mediaType = response.Content!["application/problem+json"];
            mediaType.Schema.Should().BeOfType<OpenApiSchemaReference>(
                because: $"response {statusCode} schema must be a $ref to avoid duplication");
        }
    }
}
