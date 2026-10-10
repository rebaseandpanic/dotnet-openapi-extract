using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for response-level validation rules.
/// </summary>
public sealed class ResponseRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // response.description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResponseDescription_WhenPresent_NoViolation()
    {
        var doc = BuildDocWithResponse("200", r => r.Description = "OK");
        var rule = new ResponseDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ResponseDescription_WhenMissing_OneViolation()
    {
        var doc = BuildDocWithResponse("200", r => r.Description = null);
        var rule = new ResponseDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("response.description");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // response.schema-when-body
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResponseSchemaWhenBody_WhenSchemaPresent_NoViolation()
    {
        var doc = BuildDocWithResponse("200", r =>
        {
            r.Description = "OK";
            r.Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Object }
                }
            };
        });
        var rule = new ResponseSchemaWhenBodyRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ResponseSchemaWhenBody_WhenContentButNoSchema_OneViolation()
    {
        var doc = BuildDocWithResponse("200", r =>
        {
            r.Description = "OK";
            r.Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = null }
            };
        });
        var rule = new ResponseSchemaWhenBodyRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("response.schema-when-body");
    }

    [Fact]
    public void ResponseSchemaWhenBody_WhenNoContent_NoViolation()
    {
        var doc = BuildDocWithResponse("204", r =>
        {
            r.Description = "No Content";
            r.Content = null;
        });
        var rule = new ResponseSchemaWhenBodyRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // response.description by version (OpenAPI 3.2 makes it optional)
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<OpenApiSpecVersion?, ValidationSeverity> DescriptionLevels => new()
    {
        { OpenApiSpecVersion.OpenApi3_0, ValidationSeverity.Error },
        { OpenApiSpecVersion.OpenApi3_1, ValidationSeverity.Error },
        { OpenApiSpecVersion.OpenApi3_2, ValidationSeverity.Warning },
        // Unknown version: treated as 3.0/3.1.
        { null, ValidationSeverity.Error },
    };

    [Theory]
    [MemberData(nameof(DescriptionLevels))]
    public void ResponseDescription_Missing_LevelFollowsVersion(OpenApiSpecVersion? version, ValidationSeverity expected)
    {
        var doc = BuildDocWithResponse("200", r => r.Description = null);

        new ResponseDescriptionRule().GetDefaultSeverity(version).Should().Be(expected);

        var result = CoreValidator.Validate(doc, new ValidationContext { OpenApiSpecVersion = version });

        result.SkippedRules.Should().NotContain("response.description");
        var violation = result.Violations.Should().ContainSingle(v => v.RuleId == "response.description").Subject;
        violation.Severity.Should().Be(expected);
        violation.JsonPointer.Should().Be("#/paths/~1api~1test/get/responses/200");
    }

    [Fact]
    public void ResponseDescription_V32_ErrorRuleOverride_MakesItAnError()
    {
        var result = ValidateMissingDescription(OpenApiSpecVersion.OpenApi3_2, new ValidationContext
        {
            OpenApiSpecVersion = OpenApiSpecVersion.OpenApi3_2,
            SeverityOverrides  = new Dictionary<string, ValidationSeverity> { ["response.description"] = ValidationSeverity.Error },
        });

        result.Violations.Should().ContainSingle(v => v.RuleId == "response.description")
            .Which.Severity.Should().Be(ValidationSeverity.Error);
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    public void ResponseDescription_V30V31_WarnRuleOverride_MakesItAWarning(OpenApiSpecVersion version)
    {
        var result = ValidateMissingDescription(version, new ValidationContext
        {
            OpenApiSpecVersion = version,
            SeverityOverrides  = new Dictionary<string, ValidationSeverity> { ["response.description"] = ValidationSeverity.Warning },
        });

        result.Violations.Should().ContainSingle(v => v.RuleId == "response.description")
            .Which.Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void ResponseDescription_V32_SkipRule_NoViolation()
    {
        var result = ValidateMissingDescription(OpenApiSpecVersion.OpenApi3_2, new ValidationContext
        {
            OpenApiSpecVersion = OpenApiSpecVersion.OpenApi3_2,
            SkippedRuleIds     = new HashSet<string> { "response.description" },
        });

        result.Violations.Should().NotContain(v => v.RuleId == "response.description");
        result.SkippedRules.Should().NotContain("response.description");
    }

    /// <summary>
    /// <c>--strict</c> promotes what is a warning for the document's version: a 3.2 document read by
    /// standalone <c>validate</c> reports the missing description as a warning, and as an error under
    /// <c>--strict</c>.
    /// </summary>
    [Fact]
    public async Task ResponseDescription_V32_Strict_PromotesToError()
    {
        using var tempDir = new TempDirectory();
        var spec = Path.Combine(tempDir.Path, "openapi.json");
        await File.WriteAllTextAsync(spec, """
            {
              "openapi": "3.2.0",
              "info": { "title": "T", "version": "1" },
              "paths": { "/a": { "get": { "operationId": "GetA", "responses": { "200": { } } } } }
            }
            """, TestContext.Current.CancellationToken);

        var lenient = await CliRunner.RunAsync(["validate", "--spec", spec], tempDir.Path, TestContext.Current.CancellationToken);
        var strict  = await CliRunner.RunAsync(["validate", "--spec", spec, "--strict"], tempDir.Path, TestContext.Current.CancellationToken);

        lenient.ExitCode.Should().NotBe(2, lenient.StdErr);
        strict.ExitCode.Should().Be(1, strict.StdErr);
        DescriptionSeverity(lenient).Should().Be("warning");
        DescriptionSeverity(strict).Should().Be("error");

        static string? DescriptionSeverity(CliResult run) =>
            (JsonNode.Parse(run.StdOut)!["violations"]!.AsArray()
                .Single(v => (string?)v!["rule"] == "response.description"))!["severity"]!.GetValue<string>();
    }

    private static ValidationResult ValidateMissingDescription(OpenApiSpecVersion version, ValidationContext context) =>
        CoreValidator.Validate(BuildDocWithResponse("200", r => r.Description = null), context);

    // ─────────────────────────────────────────────────────────────────────────
    // response.schema-when-body: itemSchema / x-oai-itemSchema, schema-less event streams
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string> ItemSchemaForms => new()
    {
        { "3.0.4", "x-oai-itemSchema" },
        { "3.1.2", "x-oai-itemSchema" },
        { "3.2.0", "itemSchema" },
    };

    /// <summary>A document as written for its version: the reader loads the 3.0/3.1 extension into the model.</summary>
    [Theory]
    [MemberData(nameof(ItemSchemaForms))]
    public void ResponseSchemaWhenBody_ItemSchemaOfTheVersion_IsSufficient(string openapi, string key)
    {
        var (doc, version) = ParseSequentialResponse(openapi, $$"""{ "{{key}}": { "type": "string" } }""");

        new ResponseSchemaWhenBodyRule().Validate(doc, new ValidationContext { OpenApiSpecVersion = version })
            .Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ItemSchemaForms))]
    public void ResponseSchemaWhenBody_SequentialMediaTypeWithoutAnySchema_OneViolation(string openapi, string key)
    {
        _ = key;
        var (doc, version) = ParseSequentialResponse(openapi, "{ }");

        new ResponseSchemaWhenBodyRule().Validate(doc, new ValidationContext { OpenApiSpecVersion = version })
            .Should().ContainSingle()
            .Which.JsonPointer.Should().Be("#/paths/~1a/get/responses/200/content/application~1x-ndjson");
    }

    [Theory]
    [InlineData("text/event-stream")]
    [InlineData("text/event-stream; charset=utf-8")]
    [InlineData("Text/Event-Stream")]
    public void ResponseSchemaWhenBody_EventStreamWithoutSchema_NoViolation(string mediaType)
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var doc = BuildDocWithResponse("200", r =>
            {
                r.Description = "OK";
                r.Content = new Dictionary<string, IOpenApiMediaType> { [mediaType] = new OpenApiMediaType() };
            });

            new ResponseSchemaWhenBodyRule().Validate(doc, new ValidationContext { OpenApiSpecVersion = version })
                .Should().BeEmpty(because: $"OpenAPI {version}");
        }
    }

    [Fact]
    public void ResponseSchemaWhenBody_EventStreamNextToJsonWithoutSchema_OnlyJsonViolates()
    {
        var doc = BuildDocWithResponse("200", r =>
        {
            r.Description = "OK";
            r.Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["text/event-stream"] = new OpenApiMediaType(),
                ["application/json"]  = new OpenApiMediaType(),
            };
        });

        new ResponseSchemaWhenBodyRule().Validate(doc, DefaultContext)
            .Should().ContainSingle()
            .Which.JsonPointer.Should().Be("#/paths/~1api~1test/get/responses/200/content/application~1json");
    }

    private static (OpenApiDocument Document, OpenApiSpecVersion Version) ParseSequentialResponse(string openapi, string mediaType)
    {
        var json = $$"""
            {
              "openapi": "{{openapi}}",
              "info": { "title": "T", "version": "1" },
              "paths": { "/a": { "get": { "responses": { "200": {
                "description": "OK",
                "content": { "application/x-ndjson": {{mediaType}} }
              } } } } }
            }
            """;
        var result = OpenApiDocument.Parse(json, "json", new OpenApiReaderSettings());
        result.Diagnostic!.Errors.Should().BeEmpty();
        return (result.Document!, result.Diagnostic.SpecificationVersion);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocWithResponse(string statusCode, Action<OpenApiResponse> configure)
    {
        var response = new OpenApiResponse();
        configure(response);

        var operation = new OpenApiOperation
        {
            Summary = "Test",
            OperationId = "TestOp",
            Description = "Test description that is long enough for rules.",
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Test", null) },
            Responses = new OpenApiResponses
            {
                [statusCode] = response,
                ["422"] = new OpenApiResponse { Description = "Error" },
            },
        };

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/test"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = operation
                    }
                }
            }
        };
    }
}

/// <summary>Builds ModernApi with validation once per OpenAPI version.</summary>
public sealed class ModernApiValidationFixture
{
    public ModernApiValidationFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = OpenApiDocumentBuilder.BuildWithValidation(
                VersionedDocumentHarness.ModernApiOptions(version), new ValidationContext(), out var result);
            Builds[version] = (document, result);
        }
    }

    public Dictionary<OpenApiSpecVersion, (OpenApiDocument Document, ValidationResult Result)> Builds { get; } = [];
}

/// <summary>
/// The generator's own output passes the response rules in every version: streaming responses with
/// <c>itemSchema</c> and schema-less event streams are not reported as missing a schema.
/// </summary>
public class ResponseRulesOnGeneratedOutputTests(ModernApiValidationFixture fixture) : IClassFixture<ModernApiValidationFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ModernApi_NoResponseRuleViolations(OpenApiSpecVersion version)
    {
        var (document, result) = fixture.Builds[version];

        // The document has what the rules must accept: an item schema and a schema-less event stream.
        var mediaTypes = document.Paths.Values
            .SelectMany(p => p.Operations?.Values.AsEnumerable() ?? [])
            .SelectMany(o => o.Responses?.Values.AsEnumerable() ?? [])
            .SelectMany(r => r.Content ?? new Dictionary<string, IOpenApiMediaType>())
            .ToList();
        mediaTypes.Should().Contain(m => m.Value.ItemSchema != null && m.Value.Schema == null);
        mediaTypes.Should().Contain(m => m.Key.StartsWith("text/event-stream", StringComparison.Ordinal)
            && m.Value.Schema == null && m.Value.ItemSchema == null);

        result.SkippedRules.Should().NotContain(["response.schema-when-body", "response.description"]);
        result.Violations.Should().NotContain(v => v.RuleId == "response.schema-when-body");
        result.Violations.Should().NotContain(v => v.RuleId == "response.description" && v.Severity == ValidationSeverity.Error);
    }
}
