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
}
