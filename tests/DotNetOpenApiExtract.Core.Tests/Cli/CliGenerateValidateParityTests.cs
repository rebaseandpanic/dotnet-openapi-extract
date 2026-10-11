using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Cli;

/// <summary>
/// <c>generate --validate</c> and the <c>validate</c> command honour the same validation flags:
/// the same settings over the same document give the same violations.
/// </summary>
public class CliGenerateValidateParityTests
{
    [Fact]
    public async Task SameValidationFlags_GiveSameViolations_InGenerateAndValidate()
    {
        using var tempDir = new TempDirectory();
        var spec = Path.Combine(tempDir.Path, "openapi.json");
        var generateReport = Path.Combine(tempDir.Path, "generate-report.json");
        var validateReport = Path.Combine(tempDir.Path, "validate-report.json");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Every option of the validation context that a flag sets, on rules whose result depends on it.
        string[] validationFlags =
        [
            "--enable-rule", "operation.has-required-response-codes",
            "--require-response-code", "QUERY:418",
            "--require-response-code", "TRACE:418",
            "--rule-min-length", "spec.info-description:10000",
            "--min-description-length", "7",
            "--exclude-validation-path", "/http-methods",
            "--skip-rule", "schema.description",
            "--warn-rule", "operation.has-required-response-codes",
            // Reads [Authorize] from the assembly, so it runs only in generate --validate.
            "--skip-rule", "operation.security",
        ];

        var generate = await CliRunner.RunAsync(
            [
                "--assembly", TestPaths.ModernApiDll, "--output", spec, "--openapi-version", "3.2",
                "--validate", "--validation-report", generateReport, .. validationFlags,
            ],
            tempDir.Path,
            cancellationToken);
        generate.ExitCode.Should().NotBe(2, because: generate.StdErr);

        var validate = await CliRunner.RunAsync(
            ["validate", "--spec", spec, "--validation-report", validateReport, .. validationFlags],
            tempDir.Path,
            cancellationToken);
        validate.ExitCode.Should().NotBe(2, because: validate.StdErr);

        var fromValidate = Violations(await File.ReadAllTextAsync(validateReport, cancellationToken));
        var fromGenerate = Violations(await File.ReadAllTextAsync(generateReport, cancellationToken));

        // The flags take effect in the standalone command: QUERY:418 is required on the query
        // operation, while the TRACE operation lies under the excluded /http-methods prefix.
        fromValidate.Should().Contain(
            "operation.has-required-response-codes|warning|#/paths/~1additional-methods~1search/query/responses/418");
        fromValidate.Should().NotContain(v => v.EndsWith("/trace/responses/418", StringComparison.Ordinal));
        fromValidate.Should().Contain("spec.info-description|warning|#/info");
        fromValidate.Should().NotContain(v => v.StartsWith("schema.description|", StringComparison.Ordinal));

        fromGenerate.Should().BeEquivalentTo(fromValidate);
        generate.ExitCode.Should().Be(validate.ExitCode);
    }

    /// <summary>Violations of a JSON report as <c>rule|severity|jsonPointer</c>.</summary>
    private static List<string> Violations(string reportJson)
    {
        var violations = JsonNode.Parse(reportJson)!["violations"]!.AsArray();
        return violations
            .Select(v => $"{v!["rule"]!.GetValue<string>()}|{v["severity"]!.GetValue<string>()}|{v["jsonPointer"]!.GetValue<string>()}")
            .ToList();
    }
}
