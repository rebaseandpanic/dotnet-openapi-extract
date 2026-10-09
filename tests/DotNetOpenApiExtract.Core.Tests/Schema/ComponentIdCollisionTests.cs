using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>Builds both fixtures once for the whole class: building loads the assembly.</summary>
public sealed class ComponentIdFixture
{
    public ComponentIdFixture()
    {
        var sample = VersionedDocumentHarness.Build(VersionedDocumentHarness.SampleApiOptions());
        var modern = VersionedDocumentHarness.Build(VersionedDocumentHarness.ModernApiOptions());

        SampleApi = Serialize(sample);
        ModernApi = Serialize(modern);
    }

    public JsonNode SampleApi { get; }

    public JsonNode ModernApi { get; }

    private static JsonNode Serialize(OpenApiDocument document) =>
        VersionedDocumentHarness.SerializeAsync(
                document, OpenApiSpecVersion.OpenApi3_0, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
}

/// <summary>
/// Component ids: names that did not collide keep the Swashbuckle-style id; types that share a
/// candidate id get different components, and each reference leads to the schema of its own type.
/// </summary>
public class ComponentIdCollisionTests(ComponentIdFixture fixture) : IClassFixture<ComponentIdFixture>
{
    [Theory]
    [InlineData("UserDto")]
    [InlineData("UserDtoApiResponse")]
    [InlineData("FileMetadataApiResponse")]
    [InlineData("ProblemDetails")]
    public void SampleApi_ExistingIds_AreUnchanged(string id)
    {
        Schemas(fixture.SampleApi).ContainsKey(id).Should().BeTrue();
    }

    [Fact]
    public void NonGenericTypesWithSameShortName_SecondGetsFullName()
    {
        var alpha = ResponseRef(fixture.ModernApi, "/component-ids/alpha-summary");
        var beta  = ResponseRef(fixture.ModernApi, "/component-ids/beta-summary");

        new[] { alpha, beta }.Should().BeEquivalentTo(
            ["Summary", alpha == "Summary" ? "ModernApi_Models_Beta_Summary" : "ModernApi_Models_Alpha_Summary"]);
        PropertyNames(fixture.ModernApi, alpha).Should().Equal("alphaCount");
        PropertyNames(fixture.ModernApi, beta).Should().Equal("betaLabel");
    }

    [Fact]
    public void ClosedGenericTypesWithSameCandidate_GetOwnComponents()
    {
        var alpha = ResponseRef(fixture.ModernApi, "/component-ids/alpha-page");
        var beta  = ResponseRef(fixture.ModernApi, "/component-ids/beta-page");

        alpha.Should().NotBe(beta);
        PropertyNames(fixture.ModernApi, alpha).Should().BeEquivalentTo(["alphaItems", "alphaTotal"]);
        PropertyNames(fixture.ModernApi, beta).Should().BeEquivalentTo(["betaFirst", "betaCursor"]);
    }

    private static JsonObject Schemas(JsonNode document) =>
        document["components"]!["schemas"]!.AsObject();

    private static string ResponseRef(JsonNode document, string path)
    {
        var reference = document["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!
            ["application/json"]!["schema"]!["$ref"]!.GetValue<string>();
        const string prefix = "#/components/schemas/";
        reference.Should().StartWith(prefix);
        return reference[prefix.Length..];
    }

    private static IReadOnlyList<string> PropertyNames(JsonNode document, string id) =>
        Schemas(document)[id]!["properties"]!.AsObject().Select(p => p.Key).ToList();
}
