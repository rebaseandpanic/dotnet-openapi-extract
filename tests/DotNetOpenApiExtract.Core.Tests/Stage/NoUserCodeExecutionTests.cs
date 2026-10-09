using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using ModernApi.Models.Probes;
using System.Text.Json.Nodes;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Stage;

/// <summary>
/// Extraction never executes code of the analysed assembly. ModernApi's probes (static
/// constructor, converter constructor, attribute constructor) leave a marker file under the
/// temp directory when they run. The CLI extracts ModernApi in a separate process whose temp
/// directory is fresh and private, so a marker can only come from that extraction — never from
/// code of this test process, where other tests use ModernApi types.
/// </summary>
public class NoUserCodeExecutionTests
{
    private static readonly string[] Probes =
    [
        nameof(ExecutionProbeDto),
        nameof(ExecutionProbeConverter),
        nameof(ExecutionProbeAttribute),
    ];

    [Theory]
    [InlineData("3.0")]
    [InlineData("3.1")]
    [InlineData("3.2")]
    public async Task CliExtraction_RunsNoProbe(string version)
    {
        using var workDir = new TempDirectory();
        var privateTemp = Directory.CreateDirectory(Path.Combine(workDir.Path, "tmp")).FullName;
        var output = Path.Combine(workDir.Path, "openapi.json");

        var result = await CliRunner.RunAsync(
            ["--assembly", TestPaths.ModernApiDll, "--output", output, "--openapi-version", version],
            workDir.Path,
            TestContext.Current.CancellationToken,
            environment: new Dictionary<string, string>
            {
                // Path.GetTempPath() reads TMPDIR on Unix and TMP/TEMP on Windows.
                ["TMPDIR"] = privateTemp,
                ["TMP"]    = privateTemp,
                ["TEMP"]   = privateTemp,
            });

        result.ExitCode.Should().Be(0, because: result.StdErr);
        JsonNode.Parse(File.ReadAllText(output))!["components"]!["schemas"]!.AsObject()
            .ContainsKey(nameof(ExecutionProbeDto))
            .Should().BeTrue(because: "the probes must be reached by the extraction, or the test proves nothing");

        var markerDirectory = Path.Combine(privateTemp, ProbeMarker.DirectoryName);
        Probes.Where(probe => File.Exists(Path.Combine(markerDirectory, probe)))
            .Should().BeEmpty();
    }
}
