using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Regression;

/// <summary>
/// Builds SampleApi for 3.0 once with two servers, and once more with a Program.cs that declares
/// security schemes and requirements, and serializes both.
/// </summary>
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
        Root = Serialize(document);

        using var source = new TempDirectory();
        File.WriteAllText(Path.Combine(source.Path, "Program.cs"), SecurityProgram);
        File.WriteAllText(
            Path.Combine(source.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        SecurityRoot = Serialize(VersionedDocumentHarness.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = source.Path,
        }));
    }

    /// <summary>
    /// Two declared schemes; one requirement call naming ApiKey, another naming ApiKey and Bearer:
    /// the calls are alternatives (OR), the names inside one call are combined (AND).
    /// </summary>
    private const string SecurityProgram =
        """
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key"
            });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("ApiKey"), [] }
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("ApiKey"), [] },
                { new OpenApiSecuritySchemeReference("Bearer"), [] }
            });
        });
        builder.Build().Run();
        """;

    /// <summary>SampleApi with two servers and no security declarations.</summary>
    public JsonNode Root { get; }

    /// <summary>SampleApi with the security declarations of <see cref="SecurityProgram"/>.</summary>
    public JsonNode SecurityRoot { get; }

    private static JsonNode Serialize(OpenApiDocument document) =>
        VersionedDocumentHarness.SerializeAsync(
                document, OpenApiSpecVersion.OpenApi3_0, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
}

/// <summary>
/// OpenAPI 3.0 output that must not change for existing users: the 0.16.0 nullable form, the
/// <c>allOf</c> wrapper of reference properties, operationIds of single operations, and server
/// URLs, AND/OR structure of security requirements. Expected values come from the field catalog
/// and the 0.16.0 changelog.
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
        // OAS 3.0.3 applies nullable only next to an explicit type; enum [null] keeps the branch to null.
        JsonNode.DeepEquals(branches[1], JsonNode.Parse("""{"type":"object","nullable":true,"enum":[null]}""")).Should().BeTrue(branches[1]!.ToJsonString());
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

    [Fact]
    public void DocumentRequirements_OneObjectPerCall_NamesInOneCallCombined()
    {
        // OR across AddSecurityRequirement calls, AND within one call; a declared scheme is
        // written as {"ApiKey": []}.
        JsonNode.DeepEquals(
                fixture.SecurityRoot["security"],
                JsonNode.Parse("""[{"ApiKey":[]},{"ApiKey":[],"Bearer":[]}]"""))
            .Should().BeTrue(because: fixture.SecurityRoot["security"]?.ToJsonString());
    }

    [Fact]
    public void PlainAuthorize_InheritsDocumentRequirements()
    {
        fixture.SecurityRoot["paths"]!["/api/secure"]!["get"]!.AsObject()
            .ContainsKey("security").Should().BeFalse();
    }

    [Fact]
    public void AllowAnonymous_WritesEmptyRequirementList()
    {
        JsonNode.DeepEquals(fixture.SecurityRoot["paths"]!["/api/secure/public"]!["get"]!["security"], new JsonArray())
            .Should().BeTrue();
    }

    [Fact]
    public void AuthorizeWithScheme_IsOneRequirementOnThatScheme()
    {
        JsonNode.DeepEquals(
                fixture.SecurityRoot["paths"]!["/api/secure/admin"]!["get"]!["security"],
                JsonNode.Parse("""[{"Bearer":[]}]"""))
            .Should().BeTrue();
    }
}
