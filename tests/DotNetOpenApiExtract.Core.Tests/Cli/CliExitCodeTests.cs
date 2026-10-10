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

    /// <summary>
    /// A command line that does not parse is a usage error, exit 2 like every other error: 1 is
    /// reserved for validation errors, so CI can tell a typo in a flag from a failing document.
    /// </summary>
    [Theory]
    [InlineData("validate", "spec.json")]
    [InlineData("validate", "--bogus", "x")]
    [InlineData("validate")]
    [InlineData("validate", "--spec")]
    [InlineData("--assembly")]
    [InlineData("--bogus")]
    public async Task UnparsableCommandLine_ExitsWithTwo(params string[] arguments)
    {
        using var tempDir = new TempDirectory();

        var result = await CliRunner.RunAsync(arguments, tempDir.Path, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(2, because: result.StdErr);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("validate", "--help")]
    public async Task Help_ExitsWithZero(params string[] arguments)
    {
        using var tempDir = new TempDirectory();

        var result = await CliRunner.RunAsync(arguments, tempDir.Path, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, because: result.StdErr);
    }

    /// <summary>The help of <c>--validate</c> counts the rules of the registry, and its parts add up to the total.</summary>
    [Fact]
    public async Task ValidateHelp_RuleCountsAddUpToTheRegistry()
    {
        using var tempDir = new TempDirectory();

        var result = await CliRunner.RunAsync(["--help"], tempDir.Path, TestContext.Current.CancellationToken);

        var counts = System.Text.RegularExpressions.Regex.Match(result.StdOut, @"(\d+) rules\D+?(\d+)\D+?(\d+)\D+?(\d+)");
        counts.Success.Should().BeTrue(result.StdOut);
        var (total, errors, warnings, off) = (int.Parse(counts.Groups[1].Value), int.Parse(counts.Groups[2].Value),
            int.Parse(counts.Groups[3].Value), int.Parse(counts.Groups[4].Value));

        total.Should().Be(DotNetOpenApiExtract.Core.Validation.OpenApiValidator.AllRuleIds.Count);
        off.Should().Be(DotNetOpenApiExtract.Core.Validation.OpenApiValidator.DefaultOffRuleIds.Count);
        (errors + warnings + off).Should().Be(total);
    }
}
