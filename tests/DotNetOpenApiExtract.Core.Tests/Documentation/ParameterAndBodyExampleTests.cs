using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Documentation;

/// <summary>ModernApi for every version: parsed document and diagnostics.</summary>
public sealed class ParameterAndBodyExampleFixture
{
    public ParameterAndBodyExampleFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Documents[version] = JsonNode.Parse(
                document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;
            Diagnostics[version] = diagnostics;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];
}

/// <summary>
/// XML <c>&lt;param example&gt;</c>: a path, query or header parameter gets <c>parameter.example</c>, a
/// <c>[FromBody]</c> parameter the example of the request body's media type, <c>[FromForm]</c> fields
/// one object keyed by their names in the form. Values are parsed by the schema; those that do not
/// parse are reported. XML <c>&lt;param&gt;</c> is found by the C# name of a renamed parameter.
/// </summary>
public class ParameterAndBodyExampleTests(ParameterAndBodyExampleFixture fixture) : IClassFixture<ParameterAndBodyExampleFixture>
{
    private const string Code = ExtractionDiagnosticCodes.ParameterExampleNotParsable;

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Operation(OpenApiSpecVersion version, string path, string method) =>
        fixture.Documents[version]["paths"]![path]![method]!.AsObject();

    private static JsonObject Parameter(JsonObject operation, string name) =>
        operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!.AsObject();

    private static int ParameterIndex(JsonObject operation, string name) =>
        operation["parameters"]!.AsArray().Select((p, i) => (p, i)).Single(x => x.p!["name"]!.GetValue<string>() == name).i;

    private static string Pointer(string path, string method) =>
        $"#/paths/{path.Replace("~", "~0").Replace("/", "~1")}/{method}";

    private static JsonObject MediaType(JsonObject operation, string mediaType) =>
        operation["requestBody"]!["content"]![mediaType]!.AsObject();

    private static string Json(string text) => JsonNode.Parse(text)?.ToJsonString() ?? "null";

    public static TheoryData<OpenApiSpecVersion, string, string> ParameterExamples()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "id", "42");
            data.Add(version, "ratio", "1.5");
            data.Add(version, "flag", "true");
            data.Add(version, "code", "\"123\"");
            data.Add(version, "maybe", "null");
            data.Add(version, "X-Trace", "\"abc-1\"");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ParameterExamples))]
    public void ParameterExample_HasTheSchemaJsonType_InEveryVersion(OpenApiSpecVersion version, string name, string expected)
    {
        var operation = Operation(version, "/parameter-examples/values/{id}", "get");
        var parameter = Parameter(operation, name);

        parameter.ContainsKey("example").Should().BeTrue();
        (parameter["example"]?.ToJsonString() ?? "null").Should().Be(Json(expected));
        parameter["schema"]!.AsObject().ContainsKey("example").Should().BeFalse();
        parameter["schema"]!.AsObject().ContainsKey("examples").Should().BeFalse();
        operation.ContainsKey("requestBody").Should().BeFalse();
        fixture.Diagnostics[version].Should().NotContain(d =>
            d.Location != null && d.Location.StartsWith(Pointer("/parameter-examples/values/{id}", "get"), StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void UnparsableParameterExample_IsNotWritten_OneWarningAtTheParameter(OpenApiSpecVersion version)
    {
        var operation = Operation(version, "/parameter-examples/broken", "get");

        foreach (var (name, text) in new[] { ("count", "many"), ("mandatory", "null") })
        {
            Parameter(operation, name).ContainsKey("example").Should().BeFalse(because: name);
            var location = $"{Pointer("/parameter-examples/broken", "get")}/parameters/{ParameterIndex(operation, name)}";
            var warning = fixture.Diagnostics[version].Where(d => d.Location == location).Should().ContainSingle(because: name).Which;
            warning.Code.Should().Be(Code);
            warning.Subjects.Should().Equal(name, text);
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void BodyExample_IsOnTheMediaType_NotAParameter(OpenApiSpecVersion version)
    {
        var operation = Operation(version, "/parameter-examples/body", "post");

        MediaType(operation, "application/json")["example"]!.ToJsonString().Should().Be(Json("{\"label\": \"from the body\"}"));
        operation.ContainsKey("parameters").Should().BeFalse();
        fixture.Diagnostics[version].Should().NotContain(d =>
            d.Location != null && d.Location.StartsWith(Pointer("/parameter-examples/body", "post"), StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void UnparsableBodyExample_IsNotWritten_OneWarningAtTheBody(OpenApiSpecVersion version)
    {
        foreach (var (path, text) in new[] { ("/parameter-examples/body-broken", "not json"), ("/parameter-examples/body-null", "null") })
        {
            MediaType(Operation(version, path, "post"), "application/json").ContainsKey("example").Should().BeFalse(because: path);
            var warning = fixture.Diagnostics[version].Where(d => d.Location == $"{Pointer(path, "post")}/requestBody")
                .Should().ContainSingle(because: path).Which;
            warning.Code.Should().Be(Code);
            warning.Subjects.Should().Equal("body", text);
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void FormExample_IsAnObjectKeyedByTheFieldsNamesInTheForm(OpenApiSpecVersion version)
    {
        var form = MediaType(Operation(version, "/parameter-examples/form", "post"), "multipart/form-data");
        form["example"]!.ToJsonString().Should().Be(Json("{\"doc_title\": \"Report\", \"pages\": 12}"),
            because: "the renamed field keeps its form name and the field without an example is absent");

        MediaType(Operation(version, "/parameter-examples/form-single", "post"), "multipart/form-data")["example"]!
            .ToJsonString().Should().Be(Json("{\"pages\": 3}"));
        MediaType(Operation(version, "/parameter-examples/form-complex", "post"), "multipart/form-data")["example"]!
            .ToJsonString().Should().Be(Json("{\"upload\": {\"label\": \"from the form\"}}"));

        foreach (var path in new[] { "/parameter-examples/form", "/parameter-examples/form-single", "/parameter-examples/form-complex" })
            fixture.Diagnostics[version].Should().NotContain(d =>
                d.Location != null && d.Location.StartsWith(Pointer(path, "post"), StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void UnparsableFormFields_AreLeftOut_OneWarningAtTheBody(OpenApiSpecVersion version)
    {
        const string path = "/parameter-examples/form-broken";
        var form = MediaType(Operation(version, path, "post"), "multipart/form-data");

        form["example"]!.ToJsonString().Should().Be(Json("{\"doc_title\": \"Report\"}"));
        var warning = fixture.Diagnostics[version].Where(d => d.Location == $"{Pointer(path, "post")}/requestBody")
            .Should().ContainSingle().Which;
        warning.Code.Should().Be(Code);
        warning.Subjects.Should().Equal("pages", "many", "copies", "x");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void RenamedParameters_KeepTheirXmlDescription(OpenApiSpecVersion version)
    {
        var operation = Operation(version, "/parameter-examples/renamed", "get");

        Parameter(operation, "X-A")["description"]!.GetValue<string>().Should().Be("The header a.");
        Parameter(operation, "q")["description"]!.GetValue<string>().Should().Be("The query b.");
    }

    [Fact]
    public void BodyExample_IsKept_WhenAGlobalConsumesReplacesTheMediaTypes()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers(o => o.Filters.Add(new ConsumesAttribute("application/xml")));
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);
        var options = VersionedDocumentHarness.ModernApiOptions();
        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = options.AssemblyPath,
            XmlPath      = options.XmlPath,
            SourceRoot   = tempDir.Path,
        });
        var json = JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, CancellationToken.None)
            .GetAwaiter().GetResult())!;

        var content = json["paths"]!["/parameter-examples/body"]!["post"]!["requestBody"]!["content"]!.AsObject();
        content.Select(c => c.Key).Should().Equal("application/xml");
        content["application/xml"]!["example"]!.ToJsonString().Should().Be(Json("{\"label\": \"from the body\"}"));
    }
}
