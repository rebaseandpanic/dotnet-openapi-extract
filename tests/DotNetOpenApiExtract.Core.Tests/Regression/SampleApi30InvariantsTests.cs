using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Regression;

/// <summary>Builds SampleApi for 3.0 once (with two servers) and serializes it.</summary>
public sealed class SampleApi30Fixture
{
    public static readonly IReadOnlyList<string> Servers = ["https://api.example.com", "https://staging.example.com/v1"];

    public SampleApi30Fixture()
    {
        var document = VersionedDocumentHarness.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            Servers      = Servers,
        });
        Root = VersionedDocumentHarness.SerializeAsync(
                document, OpenApiSpecVersion.OpenApi3_0, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public JsonNode Root { get; }
}

/// <summary>
/// OpenAPI 3.0 output that must not change for existing users: the 0.16.0 nullable form, the
/// <c>allOf</c> wrapper of reference properties, operationIds of single operations, and server
/// URLs. Expected values come from the field catalog and the 0.16.0 changelog.
/// </summary>
public class SampleApi30InvariantsTests(SampleApi30Fixture fixture) : IClassFixture<SampleApi30Fixture>
{
    private JsonNode Schema(string id) => fixture.Root["components"]!["schemas"]![id]!;

    [Fact]
    public void NullableValueType_IsTypeWithNullableFlag()
    {
        var age = Schema("UserProfile")["properties"]!["age"]!;

        age["type"]!.GetValue<string>().Should().Be("integer");
        age["nullable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void NullableString_HasNullableFlag()
    {
        var firstName = Schema("UserProfile")["properties"]!["firstName"]!;

        firstName["type"]!.GetValue<string>().Should().Be("string");
        firstName["nullable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void NullableReference_IsAnyOfReferenceAndNullBranch()
    {
        var profile = Schema("UserDto")["properties"]!["profile"]!;
        var branches = profile["anyOf"]!.AsArray();

        branches.Should().HaveCount(2);
        branches[0]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/UserProfile");
        JsonNode.DeepEquals(branches[1], JsonNode.Parse("""{"enum":[null],"nullable":true}""")).Should().BeTrue();
        profile.AsObject().ContainsKey("nullable").Should().BeFalse(because: "0.16.0 moved the null branch into the composite");
    }

    [Fact]
    public void ReferenceProperty_WithDescription_IsWrappedInAllOf()
    {
        var requiredInner = Schema("OuterWithRefPropertyModel")["properties"]!["requiredInner"]!;

        var allOf = requiredInner["allOf"]!.AsArray();
        allOf.Should().ContainSingle();
        allOf[0]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/InnerDto");
        requiredInner.AsObject().ContainsKey("$ref").Should().BeFalse();
        requiredInner["description"]!.GetValue<string>().Should().Be("Non-nullable inner ref description");
        Schema("InnerDto")["description"]?.GetValue<string>().Should().NotBe("Non-nullable inner ref description");
    }

    [Theory]
    [InlineData("UploadFile", "post", "/api/v1/files/upload")]
    [InlineData("DownloadFile", "get", "/api/v1/files/{id}")]
    [InlineData("GetFileMetadata", "get", "/api/v1/files/{id}/metadata")]
    [InlineData("DeleteFile", "delete", "/api/v1/files/{id}")]
    public void SingleOperation_OperationIdComesFromSwaggerOperation(string operationId, string method, string path)
    {
        fixture.Root["paths"]![path]![method]!["operationId"]!.GetValue<string>().Should().Be(operationId);
    }

    [Fact]
    public void OperationWithoutSource_HasNoOperationId()
    {
        fixture.Root["paths"]!["/api/v1/ref-property"]!["get"]!.AsObject()
            .ContainsKey("operationId").Should().BeFalse();
    }

    [Fact]
    public void Servers_AreWrittenAsUrlsInOrder_WithoutNames()
    {
        var servers = fixture.Root["servers"]!.AsArray();

        servers.Select(s => s!["url"]!.GetValue<string>()).Should().Equal(SampleApi30Fixture.Servers);
        servers.Should().AllSatisfy(s =>
        {
            s!.AsObject().ContainsKey("name").Should().BeFalse();
            s.AsObject().ContainsKey("x-oai-name").Should().BeFalse();
        });
    }
}
