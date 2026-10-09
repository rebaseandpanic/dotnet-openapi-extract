using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>ModernApi built without and with a global [Consumes] filter in Program.cs (OpenAPI 3.1 JSON).</summary>
public sealed class RequestBodyConsumesFixture
{
    public RequestBodyConsumesFixture()
    {
        WithoutGlobal = Build(null);
        WithGlobal = Build("""
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers(o => o.Filters.Add(new ConsumesAttribute("application/cbor")));
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);
    }

    public JsonNode WithoutGlobal { get; }

    public JsonNode WithGlobal { get; }

    private static JsonNode Build(string? programCs)
    {
        using var source = new TempDirectory();
        File.WriteAllText(Path.Combine(source.Path, "Program.cs"), programCs ?? """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            XmlPath        = TestPaths.ModernApiXml,
            SourceRoot     = source.Path,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_1,
        };
        return VersionedDocumentHarness.BuildAndSerializeAsync(options, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}

/// <summary>
/// The media types of a request body come from <c>[Consumes]</c>: action, then controller, then a
/// global filter; a form uses its <c>[Consumes]</c> media type, else <c>multipart/form-data</c>.
/// </summary>
public class RequestBodyConsumesTests(RequestBodyConsumesFixture fixture) : IClassFixture<RequestBodyConsumesFixture>
{
    private static JsonObject Content(JsonNode document, string path) =>
        document["paths"]![path]!["post"]!["requestBody"]!["content"]!.AsObject();

    public static TheoryData<bool, string, string[]> BodyCases => new()
    {
        { false, "/consumes/action-xml", ["application/xml"] },
        { false, "/consumes/default", ["application/json"] },
        { false, "/consumes-controller/inherits", ["application/xml", "text/xml"] },
        { false, "/consumes-controller/own", ["application/merge-patch+json"] },
        { true, "/consumes/action-xml", ["application/xml"] },
        { true, "/consumes/default", ["application/cbor"] },
        { true, "/consumes-controller/inherits", ["application/xml", "text/xml"] },
        { true, "/consumes-controller/own", ["application/merge-patch+json"] },
    };

    [Theory]
    [MemberData(nameof(BodyCases))]
    public void BodyMediaTypes_FollowActionThenControllerThenGlobal(bool globalFilter, string path, string[] expected)
    {
        var document = globalFilter ? fixture.WithGlobal : fixture.WithoutGlobal;
        var content = Content(document, path);

        content.Select(p => p.Key).Should().Equal(expected, because: path);
        foreach (var (_, media) in content)
            media!["schema"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ConsumedPayload");
    }

    [Fact]
    public void Form_WithConsumes_UsesThatMediaTypeOnly()
    {
        var content = Content(fixture.WithoutGlobal, "/consumes/form-urlencoded");

        content.Select(p => p.Key).Should().Equal("application/x-www-form-urlencoded");
        content["application/x-www-form-urlencoded"]!["schema"]!["properties"]!.AsObject().Select(p => p.Key)
            .Should().BeEquivalentTo(["name", "age"]);
    }

    [Fact]
    public void Form_WithoutConsumes_IsMultipart()
    {
        Content(fixture.WithoutGlobal, "/consumes/form-default").Select(p => p.Key).Should().Equal("multipart/form-data");
    }
}
