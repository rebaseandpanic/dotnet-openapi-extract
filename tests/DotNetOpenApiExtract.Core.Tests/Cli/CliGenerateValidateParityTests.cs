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

    /// <summary>Rules that read only the document and that no generated place of a reference or a nullable element breaks.</summary>
    private static readonly string[] RulesQuietOnThePlaces =
    [
        "schema.enum-filled", "schema.no-duplicate-enum", "schema.typed-enum", "schema.required-consistency",
        "schema.array-items", "parameter.schema-type", "parameter.optional-has-default", "spec.no-ref-siblings",
    ];

    /// <summary>
    /// The places with keywords next to a reference and with nullable elements: the properties of the
    /// models that hold them and the operations of their controllers.
    /// </summary>
    private static readonly string[] PlacePointerPrefixes =
    [
        "#/components/schemas/RefSiblingsModel/", "#/components/schemas/RefSiblingsNeighbour/",
        "#/components/schemas/NullableElementsModel/", "#/components/schemas/JsonElementsModel/",
        "#/components/schemas/ElementItemPlaces/", "#/components/schemas/ObliviousElementsModel/",
        "#/paths/~1nullable-elements~1", "#/paths/~1ref-siblings-ops~1", "#/paths/~1keywords~1ref-siblings",
    ];

    public static TheoryData<string, string> VersionsAndFormats => new()
    {
        { "3.0", "json" }, { "3.0", "yaml" }, { "3.1", "json" }, { "3.1", "yaml" }, { "3.2", "json" }, { "3.2", "yaml" },
    };

    /// <summary>
    /// On the document the extractor really writes, <c>generate --validate</c> and <c>validate</c> of the
    /// written file report the same violations, the rules of a place included, whatever the form of the
    /// places: the 3.0 <c>allOf</c> wrapper or the keywords next to <c>$ref</c> in 3.1/3.2.
    /// </summary>
    /// <remarks>
    /// The rules that need the assembly (<c>schema.property-constraints</c>, <c>schema.property-format</c>,
    /// <c>operation.security</c>) are skipped: <c>validate</c> does not run them. Operations under
    /// <c>x-oai-additionalOperations</c> (3.0/3.1) are left out of the comparison: the OpenAPI reader does
    /// not load that extension back into operations, so <c>validate</c> of a 3.0/3.1 file cannot see them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(VersionsAndFormats))]
    public async Task GeneratedDocument_GivesTheSameViolations_InGenerateAndInValidateOfTheWrittenFile(string version, string format)
    {
        using var tempDir = new TempDirectory();
        var spec = Path.Combine(tempDir.Path, $"openapi.{format}");
        var generateReport = Path.Combine(tempDir.Path, "generate-report.json");
        var validateReport = Path.Combine(tempDir.Path, "validate-report.json");
        var cancellationToken = TestContext.Current.CancellationToken;
        string[] skipped =
        [
            "--skip-rule", "schema.property-constraints", "--skip-rule", "schema.property-format", "--skip-rule", "operation.security",
        ];

        var generate = await CliRunner.RunAsync(
            [
                "--assembly", TestPaths.ModernApiDll, "--xml", TestPaths.ModernApiXml, "--output", spec,
                "--format", format, "--openapi-version", version,
                "--validate", "--validation-report", generateReport, .. skipped,
            ],
            tempDir.Path,
            cancellationToken);
        generate.ExitCode.Should().NotBe(2, because: generate.StdErr);

        var validate = await CliRunner.RunAsync(
            ["validate", "--spec", spec, "--validation-report", validateReport, .. skipped],
            tempDir.Path,
            cancellationToken);
        validate.ExitCode.Should().NotBe(2, because: validate.StdErr);

        var fromGenerate = Violations(await File.ReadAllTextAsync(generateReport, cancellationToken));
        var fromValidate = Violations(await File.ReadAllTextAsync(validateReport, cancellationToken));
        fromGenerate.Should().NotBeEmpty();
        fromValidate.Should().NotBeEmpty();

        // A reference with a sibling ([Obsolete]) and no XML <summary> has no description of its own,
        // and the component's description does not count for it.
        const string undescribed = "schema.property-description|error|#/components/schemas/RefSiblingsModel/properties/obsoleted";
        fromGenerate.Should().Contain(undescribed);
        fromValidate.Should().Contain(undescribed);

        static bool OnAPlace(string violation) =>
            RulesQuietOnThePlaces.Contains(violation.Split('|')[0])
            && PlacePointerPrefixes.Any(prefix => violation.Split('|')[2].StartsWith(prefix, StringComparison.Ordinal));
        fromGenerate.Where(OnAPlace).Should().BeEmpty();
        fromValidate.Where(OnAPlace).Should().BeEmpty();

        static bool UnderAdditionalOperations(string violation) =>
            violation.Contains("/x-oai-additionalOperations/", StringComparison.Ordinal);
        fromValidate.Where(UnderAdditionalOperations).Should().BeEmpty();
        fromGenerate.Where(v => !UnderAdditionalOperations(v)).Should().BeEquivalentTo(fromValidate);
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
