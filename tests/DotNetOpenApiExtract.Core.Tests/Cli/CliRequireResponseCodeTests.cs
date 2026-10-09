using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Cli;

/// <summary><c>--require-response-code</c> accepts QUERY and TRACE and applies them to those operations.</summary>
public class CliRequireResponseCodeTests
{
    [Fact]
    public async Task QueryAndTraceFilters_AreAcceptedAndApplied()
    {
        using var tempDir = new TempDirectory();
        var spec = Path.Combine(tempDir.Path, "openapi.json");
        var cancellationToken = TestContext.Current.CancellationToken;

        var generate = await CliRunner.RunAsync(
            ["--assembly", TestPaths.ModernApiDll, "--output", spec, "--openapi-version", "3.2"],
            tempDir.Path,
            cancellationToken);
        generate.ExitCode.Should().Be(0, because: generate.StdErr);

        var result = await CliRunner.RunAsync(
            [
                "validate", "--spec", spec,
                "--enable-rule", "operation.has-required-response-codes",
                "--require-response-code", "QUERY:418",
                "--require-response-code", "TRACE:418",
            ],
            tempDir.Path,
            cancellationToken);

        result.ExitCode.Should().NotBe(2, because: result.StdErr);
        result.StdErr.Should().NotContain("unknown method filter");

        var pointers = StringValues(JsonNode.Parse(result.StdOut)).ToList();
        // A 3.2 document: the reader loads query operations natively (it does not read 3.0/3.1
        // x-oai-additionalOperations back into operations).
        pointers.Should().Contain("#/paths/~1additional-methods~1search/query/responses/418");
        pointers.Should().Contain("#/paths/~1http-methods~1trace/trace/responses/418");
    }

    private static IEnumerable<string> StringValues(JsonNode? node) => node switch
    {
        JsonObject obj   => obj.SelectMany(p => StringValues(p.Value)),
        JsonArray array  => array.SelectMany(StringValues),
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        _ => [],
    };
}
