using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Cli;

/// <summary>
/// The CLI exit-code contract: 0 on success, 2 on errors (1 is reserved for validation
/// errors). Runs the built CLI as a process.
/// </summary>
public class CliExitCodeTests
{
    [Fact]
    public async Task Generate_SampleApi_ExitsWithZero()
    {
        using var tempDir = new TempDirectory();
        var output = Path.Combine(tempDir.Path, "openapi.json");

        var result = await CliRunner.RunAsync(
            ["--assembly", TestPaths.SampleApiDll, "--output", output],
            tempDir.Path,
            TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, because: result.StdErr);
        File.Exists(output).Should().BeTrue();
    }

    [Fact]
    public async Task Generate_MissingAssembly_ExitsWithTwo()
    {
        using var tempDir = new TempDirectory();
        var missing = Path.Combine(tempDir.Path, "DoesNotExist.dll");

        var result = await CliRunner.RunAsync(
            ["--assembly", missing, "--output", Path.Combine(tempDir.Path, "openapi.json")],
            tempDir.Path,
            TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(2);
    }
}
