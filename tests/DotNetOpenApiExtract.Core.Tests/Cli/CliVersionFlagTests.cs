using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Cli;

/// <summary>
/// <c>--openapi-version</c> accepts only 3.0, 3.1 and 3.2; any other value is a configuration
/// error with exit code 2, never a silent 3.0.
/// </summary>
public class CliVersionFlagTests
{
    [Theory]
    [InlineData("4.0")]
    [InlineData("2.0")]
    public async Task Generate_UnsupportedVersion_ExitsWithTwoAndWritesNoDocument(string version)
    {
        using var tempDir = new TempDirectory();
        var output = Path.Combine(tempDir.Path, "openapi.json");

        var result = await CliRunner.RunAsync(
            ["--assembly", TestPaths.SampleApiDll, "--output", output, "--openapi-version", version],
            tempDir.Path,
            TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(2);
        File.Exists(output).Should().BeFalse();
    }
}
