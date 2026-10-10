using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// The fixture documents written to files in every version and format, and the report of a
/// standalone <c>validate --spec</c> run on each file (the CLI process, as in CI).
/// </summary>
public sealed class GeneratedFileValidationFixture : IAsyncLifetime
{
    private readonly TempDirectory _directory = new();

    /// <summary>The written JSON documents, by fixture and version.</summary>
    public Dictionary<(string Fixture, OpenApiSpecVersion Version), JsonNode> Documents { get; } = [];

    /// <summary>Report of <c>validate --spec</c>, by fixture, version and format.</summary>
    public Dictionary<(string Fixture, OpenApiSpecVersion Version, DocumentFormat Format), JsonNode> Reports { get; } = [];

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixtures = new (string Name, Func<OpenApiSpecVersion, OpenApiDocumentOptions> Options)[]
        {
            ("ModernApi", VersionedDocumentHarness.ModernApiOptions),
            ("SampleApi", VersionedDocumentHarness.SampleApiOptions),
        };

        foreach (var (name, options) in fixtures)
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = VersionedDocumentHarness.Build(options(version));
            foreach (var format in new[] { DocumentFormat.Json, DocumentFormat.Yaml })
            {
                var spec = Path.Combine(_directory.Path, $"{name}-{version}.{(format == DocumentFormat.Json ? "json" : "yaml")}");
                var text = format == DocumentFormat.Json
                    ? await document.SerializeAsJsonAsync(version, ct)
                    : await document.SerializeAsYamlAsync(version, ct);
                await File.WriteAllTextAsync(spec, text, ct);
                if (format == DocumentFormat.Json)
                    Documents[(name, version)] = JsonNode.Parse(text)!;

                var result = await CliRunner.RunAsync(["validate", "--spec", spec], _directory.Path, ct);
                if (result.ExitCode == 2)
                    throw new InvalidOperationException($"validate failed on {spec}: {result.StdErr}");
                Reports[(name, version, format)] = JsonNode.Parse(result.StdOut)!;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A file the extractor writes passes its own standalone check: <c>validate --spec</c> reports no
/// violation of a rule the generator satisfies by construction, and the report does not depend on
/// whether the same document was written as JSON or YAML.
/// </summary>
public class GeneratedFileValidationTests(GeneratedFileValidationFixture fixture) : IClassFixture<GeneratedFileValidationFixture>
{
    public static TheoryData<string, OpenApiSpecVersion, DocumentFormat> Files
    {
        get
        {
            var data = new TheoryData<string, OpenApiSpecVersion, DocumentFormat>();
            foreach (var name in new[] { "ModernApi", "SampleApi" })
            foreach (var version in VersionedDocumentHarness.Versions)
            foreach (var format in new[] { DocumentFormat.Json, DocumentFormat.Yaml })
                data.Add(name, version, format);
            return data;
        }
    }

    private static List<string> Pointers(JsonNode report, string rule) =>
        report["violations"]!.AsArray()
            .Where(v => (string?)v!["rule"] == rule)
            .Select(v => v!["jsonPointer"]!.GetValue<string>())
            .ToList();

    /// <summary>
    /// The generator wraps a property reference with siblings in <c>allOf</c> for 3.0; a clean
    /// <c>$ref</c> to a component that has a description, title or extensions has no siblings.
    /// </summary>
    [Theory]
    [MemberData(nameof(Files))]
    public void GeneratedFile_HasNoRefSiblings(string name, OpenApiSpecVersion version, DocumentFormat format)
    {
        var report = fixture.Reports[(name, version, format)];

        report["summary"]!["skippedRules"]!.AsArray().Select(r => r!.GetValue<string>())
            .Should().NotContain("spec.no-ref-siblings");
        Pointers(report, "spec.no-ref-siblings").Should().BeEmpty();
    }

    /// <summary>The generator writes enum values of the schema's own type, in every format.</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public void GeneratedFile_HasNoTypedEnumViolations(string name, OpenApiSpecVersion version, DocumentFormat format)
    {
        Pointers(fixture.Reports[(name, version, format)], "schema.typed-enum").Should().BeEmpty();
    }

    public static TheoryData<string, OpenApiSpecVersion> Documents
    {
        get
        {
            var data = new TheoryData<string, OpenApiSpecVersion>();
            foreach (var name in new[] { "ModernApi", "SampleApi" })
            foreach (var version in VersionedDocumentHarness.Versions)
                data.Add(name, version);
            return data;
        }
    }

    /// <summary>One document written as JSON and as YAML gets the same violations.</summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void Report_DoesNotDependOnTheFormat(string name, OpenApiSpecVersion version)
    {
        static List<string> Violations(JsonNode report) =>
            report["violations"]!.AsArray()
                .Select(v => $"{v!["rule"]!.GetValue<string>()} {v["jsonPointer"]!.GetValue<string>()}")
                .Order(StringComparer.Ordinal)
                .ToList();

        Violations(fixture.Reports[(name, version, DocumentFormat.Yaml)])
            .Should().Equal(Violations(fixture.Reports[(name, version, DocumentFormat.Json)]));
    }

    /// <summary>
    /// A value of another JSON type is still reported, in both formats: <c>"0"</c> (a string) and
    /// <c>1.5</c> under <c>type: integer</c>; <c>2.0</c> is an integer.
    /// </summary>
    [Theory]
    [InlineData("json", """
        {"openapi":"3.0.4","info":{"title":"T","version":"1"},"paths":{},
         "components":{"schemas":{"Mode":{"type":"integer","enum":["0",1,1.5,2.0]}}}}
        """)]
    [InlineData("yaml", """
        openapi: 3.0.4
        info: {title: T, version: '1'}
        paths: {}
        components:
          schemas:
            Mode:
              type: integer
              enum: ['0', 1, 1.5, 2.0]
        """)]
    public async Task MismatchedEnumValues_AreReported_InEitherFormat(string extension, string content)
    {
        using var directory = new TempDirectory();
        var spec = Path.Combine(directory.Path, $"openapi.{extension}");
        await File.WriteAllTextAsync(spec, content, TestContext.Current.CancellationToken);

        var result = await CliRunner.RunAsync(["validate", "--spec", spec], directory.Path, TestContext.Current.CancellationToken);

        result.ExitCode.Should().NotBe(2, result.StdErr);
        Pointers(JsonNode.Parse(result.StdOut)!, "schema.typed-enum")
            .Should().Equal("#/components/schemas/Mode/enum/0", "#/components/schemas/Mode/enum/2");
    }

    /// <summary>
    /// OpenAPI 3.0.3, Schema Object, <c>nullable</c>: "A true value adds "null" to the allowed type
    /// specified by the type keyword, only if type is explicitly defined within the same Schema
    /// Object." Every <c>nullable: true</c> of a written 3.0 document stands next to a <c>type</c>, so
    /// that it admits <c>null</c>.
    /// </summary>
    [Theory]
    [InlineData("ModernApi")]
    [InlineData("SampleApi")]
    public void Generated30File_HasNullableOnlyNextToAType(string name)
    {
        var document = fixture.Documents[(name, OpenApiSpecVersion.OpenApi3_0)];
        var nullable = new List<string>();
        var untyped = new List<string>();

        void Walk(JsonNode? node, string pointer)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["nullable"] is JsonValue flag && flag.GetValueKind() == System.Text.Json.JsonValueKind.True)
                    {
                        nullable.Add(pointer);
                        if (!obj.ContainsKey("type"))
                            untyped.Add(pointer);
                    }
                    foreach (var (key, child) in obj)
                        Walk(child, $"{pointer}/{key.Replace("~", "~0").Replace("/", "~1")}");
                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                        Walk(array[i], $"{pointer}/{i}");
                    break;
            }
        }

        Walk(document, "#");

        nullable.Should().NotBeEmpty(because: "the fixture has nullable references, unions and values");
        untyped.Should().BeEmpty();
    }
}
