using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>Builds ModernApi and SampleApi once per OpenAPI version (JSON output).</summary>
public sealed class BinaryContentFixture
{
    public BinaryContentFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            ModernApi[version] = VersionedDocumentHarness.BuildAndSerializeAsync(
                VersionedDocumentHarness.ModernApiOptions(version), DocumentFormat.Json, CancellationToken.None).GetAwaiter().GetResult();
            SampleApi[version] = VersionedDocumentHarness.BuildAndSerializeAsync(
                VersionedDocumentHarness.SampleApiOptions(version), DocumentFormat.Json, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> ModernApi { get; } = [];

    /// <summary>ModernApi (3.1) with a global [Produces] filter in Program.cs.</summary>
    public JsonNode ModernApiWithGlobalProduces { get; } = BuildWithGlobalProduces();

    private static JsonNode BuildWithGlobalProduces()
    {
        using var source = new TempDirectory();
        File.WriteAllText(Path.Combine(source.Path, "Program.cs"), """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute("application/vnd.global+json")));
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

    public Dictionary<OpenApiSpecVersion, JsonNode> SampleApi { get; } = [];
}

/// <summary>
/// File content is a binary string under its declared media type (or <c>application/octet-stream</c>)
/// in every version, with no component for the framework type; <c>[SwaggerResponse]</c> media types
/// are the response's content keys.
/// </summary>
public class BinaryAndSwaggerResponseContentTests(BinaryContentFixture fixture) : IClassFixture<BinaryContentFixture>
{
    public static TheoryData<OpenApiSpecVersion, string, string> FileResponses
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, string>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                data.Add(version, "/binary/file-result", "application/octet-stream");
                data.Add(version, "/binary/file-stream", "application/octet-stream");
                data.Add(version, "/binary/file-content", "application/pdf");
                data.Add(version, "/binary/http-file-content", "application/octet-stream");
                data.Add(version, "/binary/http-file-stream", "application/octet-stream");
                data.Add(version, "/binary/stream", "application/octet-stream");
            }
            return data;
        }
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private static void ShouldBeBinary(JsonNode? schema, string because)
    {
        schema.Should().NotBeNull(because: because);
        schema!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["type", "format"], because: because);
        schema["type"]!.GetValue<string>().Should().Be("string", because: because);
        schema["format"]!.GetValue<string>().Should().Be("binary", because: because);
    }

    [Theory]
    [MemberData(nameof(FileResponses))]
    public void FileResponse_IsABinaryStringUnderItsMediaType(OpenApiSpecVersion version, string path, string mediaType)
    {
        var content = fixture.ModernApi[version]["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!.AsObject();

        content.Select(p => p.Key).Should().Equal([mediaType], because: path);
        ShouldBeBinary(content[mediaType]!["schema"], path);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void FormFile_IsABinaryFormField(OpenApiSpecVersion version)
    {
        var form = fixture.ModernApi[version]["paths"]!["/binary/upload"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;

        ShouldBeBinary(form["properties"]!["document"], "IFormFile in a form");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Components_HaveNoFrameworkFileTypes(OpenApiSpecVersion version)
    {
        foreach (var document in new[] { fixture.ModernApi[version], fixture.SampleApi[version] })
        {
            document["components"]!["schemas"]!.AsObject().Select(p => p.Key).Should().NotContain(
                ["FileResult", "FileStreamResult", "FileContentResult", "FileContentHttpResult", "FileStreamHttpResult",
                 "Stream", "IFormFile", "EntityTagHeaderValue"]);
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SampleApi_DownloadAndUpload_AreBinary(OpenApiSpecVersion version)
    {
        var paths = fixture.SampleApi[version]["paths"]!;

        var download = paths["/api/v1/files/{id}"]!["get"]!["responses"]!["200"]!["content"]!.AsObject();
        download.Select(p => p.Key).Should().Equal("application/octet-stream");
        ShouldBeBinary(download["application/octet-stream"]!["schema"], "Download");

        var upload = paths["/api/v1/files/upload"]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;
        ShouldBeBinary(upload["properties"]!["file"], "Upload");
    }

    // ── [SwaggerResponse] media types ───────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SwaggerResponseContentTypes_AreExactlyTheContentKeys(OpenApiSpecVersion version)
    {
        var response = fixture.ModernApi[version]["paths"]!["/binary/report"]!["get"]!["responses"]!["200"]!;
        var content = response["content"]!.AsObject();

        content.Select(p => p.Key).Should().Equal("application/xml", "text/csv");
        foreach (var (_, media) in content)
            media!["schema"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/ReportRow");
        response["description"]!.GetValue<string>().Should().Be("Report rows");
    }

    [Theory]
    [InlineData("/binary/report", new[] { "application/xml", "text/csv" })]
    [InlineData("/binary/report-csv", new[] { "text/csv" })]
    [InlineData("/binary/file-stream", new[] { "application/octet-stream" })]
    [InlineData("/binary/report-default", new[] { "application/vnd.global+json" })]
    public void GlobalProducesFilter_DoesNotOverrideMediaTypesOfTheResponseItself(string path, string[] expected)
    {
        var content = fixture.ModernApiWithGlobalProduces["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!.AsObject();

        content.Select(p => p.Key).Should().Equal(expected, because: path);
    }
}
