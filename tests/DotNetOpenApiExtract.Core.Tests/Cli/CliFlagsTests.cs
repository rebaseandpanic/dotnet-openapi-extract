using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Cli;

/// <summary>
/// The document metadata flags reach the Core options; wrong combinations exit with code 2; the
/// version flag drives the build, the validation and the serialization; extraction warnings never
/// change the exit code.
/// </summary>
public class CliFlagsTests
{
    private const string Dialect31 = "https://spec.openapis.org/oas/3.1/dialect/base";
    private const string Dialect32 = "https://spec.openapis.org/oas/3.2/dialect/2025-09-17";

    /// <summary>
    /// Every rule the ModernApi fixture already breaks, so that a validation run of it is clean. This
    /// isolates the fixture's existing violations for the exit-code test; it is not a list of rules a
    /// change may switch off — a new violation of the fixture must be fixed, not added here.
    /// </summary>
    private static readonly string[] RulesModernApiBreaks =
    [
        "operation.description", "operation.has-error-response", "operation.operation-id", "operation.operation-id-unique",
        "operation.request-body-description", "operation.security", "operation.success-response", "operation.summary",
        "parameter.description", "parameter.optional-has-default", "response.schema-when-body", "schema.property-constraints",
        "schema.property-description", "schema.property-format", "schema.required-consistency", "schema.typed-enum",
    ];

    private static async Task<(CliResult Result, JsonNode? Document)> Run(TempDirectory directory, params string[] flags)
    {
        var output = Path.Combine(directory.Path, "openapi.json");
        var result = await CliRunner.RunAsync(
            ["--assembly", TestPaths.ModernApiDll, "--output", output, .. flags],
            directory.Path,
            TestContext.Current.CancellationToken);
        var document = File.Exists(output) ? JsonNode.Parse(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken)) : null;
        return (result, document);
    }

    [Fact]
    public async Task MetadataFlags_ReachTheDocument()
    {
        using var directory = new TempDirectory();

        var (result, document) = await Run(directory,
            "--openapi-version", "3.2",
            "--summary", "Flag summary",
            "--license-name", "MIT License", "--license-identifier", "MIT",
            "--server", "https://prod.example.com", "--server-name", "prod",
            "--server", "https://staging.example.com", "--server-name", "staging",
            "--self-url", "https://example.com/openapi.json",
            "--json-schema-dialect", Dialect32);

        result.ExitCode.Should().Be(0, result.StdErr);
        document!["openapi"]!.GetValue<string>().Should().Be("3.2.0");
        document["info"]!["summary"]!.GetValue<string>().Should().Be("Flag summary");
        document["info"]!["license"]!.ToJsonString().Should().Be("""{"name":"MIT License","identifier":"MIT"}""");
        document["servers"]!.AsArray().Select(s => (s!["url"]!.GetValue<string>(), s["name"]!.GetValue<string>()))
            .Should().Equal(("https://prod.example.com", "prod"), ("https://staging.example.com", "staging"));
        document["$self"]!.GetValue<string>().Should().Be("https://example.com/openapi.json");
        document["jsonSchemaDialect"]!.GetValue<string>().Should().Be(Dialect32);
    }

    public static TheoryData<string[]> WrongConfigurations => new()
    {
        new[] { "--license-name", "MIT", "--license-identifier", "MIT", "--license-url", "https://opensource.org/licenses/MIT" },
        new[] { "--license-identifier", "MIT" },
        new[] { "--license-url", "https://opensource.org/licenses/MIT" },
        new[] { "--server", "https://a.example.com", "--server", "https://b.example.com", "--server-name", "a" },
        new[] { "--server-name", "a" },
        new[] { "--server", "https://a.example.com", "--server-name", " " },
        new[] { "--server", "https://a.example.com", "--server", "https://b.example.com", "--server-name", "a", "--server-name", "a" },
        new[] { "--self-url", "https://example.com/openapi.json#top" },
        new[] { "--self-url", "http://[bad" },
        new[] { "--openapi-version", "3.1", "--json-schema-dialect", "https://json-schema.org/draft/2020-12/schema" },
        new[] { "--openapi-version", "3.1", "--json-schema-dialect", Dialect32 },
        new[] { "--openapi-version", "3.2", "--json-schema-dialect", Dialect31 },
    };

    [Theory]
    [MemberData(nameof(WrongConfigurations))]
    public async Task WrongConfiguration_ExitsWithTwo_AndWritesNoDocument(string[] flags)
    {
        using var directory = new TempDirectory();

        var (result, document) = await Run(directory, flags);

        result.ExitCode.Should().Be(2, result.StdErr);
        document.Should().BeNull();
    }

    [Fact]
    public async Task ExtractionError_ExitsWithTwo()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), """
            builder.Services.AddSwaggerGen(c => c.AddSecurityDefinition("oauth", new OpenApiSecurityScheme { Type = SecuritySchemeType.OAuth2 }));
            """);

        var (result, _) = await Run(directory, "--source-root", directory.Path);

        result.ExitCode.Should().Be(2);
        result.StdErr.Should().Contain("Flows");
    }

    [Fact]
    public async Task ServerUrlWithEquals_IsReadWhole_WithoutAName()
    {
        using var directory = new TempDirectory();

        var (result, document) = await Run(directory, "--openapi-version", "3.2", "--server", "https://h.example.com/x?a=b");

        result.ExitCode.Should().Be(0, result.StdErr);
        var server = document!["servers"]!.AsArray().Should().ContainSingle().Subject!.AsObject();
        server["url"]!.GetValue<string>().Should().Be("https://h.example.com/x?a=b");
        server.ContainsKey("name").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "3.0.4")]
    [InlineData("3.1", "3.1.2")]
    [InlineData("3.2", "3.2.0")]
    public async Task VersionFlag_DrivesTheBuildAndTheSerialization(string? version, string openapi)
    {
        using var directory = new TempDirectory();

        var (result, document) = await Run(directory, version == null ? [] : ["--openapi-version", version]);

        result.ExitCode.Should().Be(0, result.StdErr);
        document!["openapi"]!.GetValue<string>().Should().Be(openapi);
        var data = document["components"]!["schemas"]!["Base64Payload"]!["properties"]!["data"]!.AsObject();
        if (version == null)
        {
            data["format"]!.GetValue<string>().Should().Be("byte");
        }
        else
        {
            data["contentEncoding"]!.GetValue<string>().Should().Be("base64", because: "the build, not only the serializer, follows the version");
            data.ContainsKey("format").Should().BeFalse();
        }
    }

    [Fact]
    public async Task ExtractionWarnings_DoNotChangeTheExitCode_StrictOnlyActsOnValidation()
    {
        using var directory = new TempDirectory();
        var skips = RulesModernApiBreaks.SelectMany(rule => new[] { "--skip-rule", rule }).ToArray();

        // A clean validation run of a 3.0 build that has extraction warnings: strict does not count them.
        var (strictClean, _) = await Run(directory, ["--validate", "--strict", "--description", "Described", .. skips]);
        strictClean.ExitCode.Should().Be(0, strictClean.StdOut);
        strictClean.StdErr.Should().Contain("Warning:");

        // Control: the same run with a warning-level violation (no info.description) — strict makes it fail.
        var (strictViolation, _) = await Run(directory, ["--validate", "--strict", .. skips]);
        strictViolation.ExitCode.Should().Be(1, strictViolation.StdOut);
        var (lenientViolation, _) = await Run(directory, ["--validate", .. skips]);
        lenientViolation.ExitCode.Should().Be(0, because: "the violation is a warning without --strict");

        // Without validation, extraction warnings alone never change the exit code.
        var (plain, _) = await Run(directory);
        plain.ExitCode.Should().Be(0);
        plain.StdErr.Should().Contain("Warning:");
    }
}
