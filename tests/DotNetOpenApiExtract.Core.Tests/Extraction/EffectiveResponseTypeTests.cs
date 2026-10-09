using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>Builds ModernApi once (OpenAPI 3.0 JSON) for the response type tests.</summary>
public sealed class EffectiveResponseTypeFixture
{
    public EffectiveResponseTypeFixture()
    {
        Document = VersionedDocumentHarness.BuildAndSerializeAsync(
                VersionedDocumentHarness.ModernApiOptions(OpenApiSpecVersion.OpenApi3_0), DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public JsonNode Document { get; }
}

/// <summary>
/// The type and description of a response follow the priority of the sources: the type declared for
/// the status code → <c>[Produces(typeof(T))]</c> / <c>[Produces&lt;T&gt;]</c> for 200 → the return
/// type, with the action winning over the controller at every level.
/// </summary>
public class EffectiveResponseTypeTests(EffectiveResponseTypeFixture fixture) : IClassFixture<EffectiveResponseTypeFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";

    private JsonObject Responses(string path) =>
        fixture.Document["paths"]![path]!["get"]!["responses"]!.AsObject();

    private string? BodyComponent(string path, string code)
    {
        var response = Responses(path)[code];
        response.Should().NotBeNull(because: $"{path} must have a {code} response");
        var reference = response!["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
        if (reference == null)
            return null;
        reference.Should().StartWith(SchemasPrefix);
        return reference[SchemasPrefix.Length..];
    }

    private string Description(string path, string code) =>
        Responses(path)[code]!["description"]!.GetValue<string>();

    public static TheoryData<string, string> SuccessBodyCases => new()
    {
        { "/response-types/declared-over-produces", "DeclaredBody" },
        { "/response-types/swagger-response-over-produces", "DeclaredBody" },
        { "/response-types/generic-declared", "DeclaredBody" },
        { "/response-types/produces-over-signature", "ProducedBody" },
        { "/response-types/generic-produces", "ProducedBody" },
        { "/response-types/untyped-declared-produces", "ProducedBody" },
        { "/response-types/untyped-declared-signature", "SignatureBody" },
        { "/response-types/signature-only", "SignatureBody" },
        { "/response-types/produces-with-error", "ProducedBody" },
        { "/controller-responses/inherits", "ControllerProducedBody" },
        { "/controller-responses/own-produces", "ProducedBody" },
        { "/controller-responses/own-not-found", "ControllerProducedBody" },
        { "/controller-declared/over-action-produces", "ControllerDeclaredBody" },
        { "/controller-declared/action-declared", "DeclaredBody" },
    };

    [Theory]
    [MemberData(nameof(SuccessBodyCases))]
    public void SuccessResponse_HasTheBodyOfTheHighestPrioritySource(string path, string expectedComponent)
    {
        BodyComponent(path, "200").Should().Be(expectedComponent);
    }

    [Fact]
    public void ProducesWithType_DeclaresThe200ResponseNextToOtherDeclaredCodes()
    {
        Responses("/response-types/produces-with-error").Select(r => r.Key)
            .Should().BeEquivalentTo(["200", "404"]);
        BodyComponent("/response-types/produces-with-error", "404").Should().Be("ActionError");
    }

    [Fact]
    public void SwaggerResponseDescription_IsKept()
    {
        Description("/response-types/swagger-response-over-produces", "200").Should().Be("Swagger described");
    }

    // ── Controller-level [ProducesResponseType] and XML <response> ─────────────

    [Fact]
    public void ControllerResponses_AppearOnEveryOperationOfTheController()
    {
        foreach (var path in new[] { "/controller-responses/inherits", "/controller-responses/own-produces" })
        {
            BodyComponent(path, "404").Should().Be("ControllerError", because: path);
            Description(path, "404").Should().Be("Controller says not found.", because: path);
            BodyComponent(path, "409").Should().Be("ControllerError", because: path);
            Description(path, "409").Should().Be("Controller says conflict.", because: path);
        }
    }

    [Fact]
    public void ActionDeclaration_WinsOverTheControllerForTheSameCode()
    {
        BodyComponent("/controller-responses/own-not-found", "404").Should().Be("ActionError");
        Description("/controller-responses/own-not-found", "404").Should().Be("Action says not found.");
    }

    [Fact]
    public void ActionDeclarationWithoutAType_TakesTheTypeAndDescriptionFromTheController()
    {
        BodyComponent("/controller-responses/untyped-conflict", "409").Should().Be("ControllerError");
        Description("/controller-responses/untyped-conflict", "409").Should().Be("Controller says conflict.");
    }

    [Fact]
    public void ControllerWithoutResponseAttributes_KeepsTheInferredResponse()
    {
        // ResponseTypeSourcesController declares nothing at class level: nothing is added.
        Responses("/response-types/signature-only").Select(r => r.Key).Should().BeEquivalentTo(["200"]);
    }

    [Fact]
    public void ControllerDeclaration_StopsTheReturnTypeFromAddingA200()
    {
        Responses("/controller-error-only/typed").Select(r => r.Key).Should().BeEquivalentTo(["404"],
            because: "ApiExplorer infers the 200 from the return type only when nothing declares a response");
    }
}
