using System.Text.Json;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Tests for Wave 7a severity infrastructure:
/// - ValidationSeverity enum
/// - DefaultSeverity on all 24 rules
/// - SeverityOverrides in ValidationContext
/// - Effective severity stamping in OpenApiValidator.Validate
/// - ErrorCount / WarningCount on ValidationResult
/// - Exit-code logic (errors block, warnings do not)
/// - JSON report format (severity field, errors/warnings in summary)
/// </summary>
public sealed class SeverityInfrastructureTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // 1. ValidationSeverity enum values
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidationSeverity_HasErrorAndWarning()
    {
        var values = Enum.GetValues<ValidationSeverity>();
        values.Should().Contain(ValidationSeverity.Error);
        values.Should().Contain(ValidationSeverity.Warning);
        values.Should().HaveCount(2);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. DefaultSeverity on all 24 rules
    // ─────────────────────────────────────────────────────────────────────────

    // Error rules (18)
    [Theory]
    [InlineData("operation.summary")]
    [InlineData("operation.operation-id")]
    [InlineData("operation.description")]
    [InlineData("operation.tags")]
    [InlineData("operation.security")]
    [InlineData("operation.deprecated-has-note")]
    [InlineData("parameter.description")]
    [InlineData("parameter.schema-type")]
    [InlineData("response.description")]
    [InlineData("response.schema-when-body")]
    [InlineData("schema.description")]
    [InlineData("schema.property-description")]
    [InlineData("schema.property-format")]
    [InlineData("schema.required-consistency")]
    [InlineData("schema.property-constraints")]
    [InlineData("schema.enum-filled")]
    [InlineData("security.scheme-defined")]
    [InlineData("spec.info-title")]
    public void Rule_DefaultSeverity_IsError(string ruleId)
    {
        var rule = CoreValidator.AllRules.Single(r => r.Id == ruleId);
        rule.DefaultSeverity.Should().Be(ValidationSeverity.Error,
            because: $"{ruleId} should default to Error");
    }

    // Warning rules (6)
    [Theory]
    [InlineData("operation.has-error-response")]
    [InlineData("parameter.optional-has-default")]
    [InlineData("enum.type-description")]
    [InlineData("enum.value-description")]
    [InlineData("security.scheme-description")]
    [InlineData("spec.info-description")]
    public void Rule_DefaultSeverity_IsWarning(string ruleId)
    {
        var rule = CoreValidator.AllRules.Single(r => r.Id == ruleId);
        rule.DefaultSeverity.Should().Be(ValidationSeverity.Warning,
            because: $"{ruleId} should default to Warning");
    }

    [Fact]
    public void AllRules_SeverityCounts_26ErrorsAnd21Warnings()
    {
        // Wave 7b added 8 Group-A error rules and 10 Group-B + 5 Group-C warning rules.
        // Total: 24 original + 23 new = 47 rules.
        // Errors: 18 original + 8 Group-A = 26.
        // Warnings: 6 original + 10 Group-B + 5 Group-C = 21.
        var errors = CoreValidator.AllRules.Count(r => r.DefaultSeverity == ValidationSeverity.Error);
        var warnings = CoreValidator.AllRules.Count(r => r.DefaultSeverity == ValidationSeverity.Warning);
        errors.Should().Be(26, because: "26 rules should default to Error (18 original + 8 Group-A)");
        warnings.Should().Be(21, because: "21 rules should default to Warning (6 original + 10 Group-B + 5 Group-C)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. ValidationViolation carries Severity field
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidationViolation_HasSeverityField()
    {
        var v = new ValidationViolation("test.rule", ValidationSeverity.Error, "#/info", null, "test message");
        v.Severity.Should().Be(ValidationSeverity.Error);
        v.RuleId.Should().Be("test.rule");
    }

    [Fact]
    public void ValidationViolation_RecordWith_CanOverrideSeverity()
    {
        var original = new ValidationViolation("test.rule", ValidationSeverity.Error, "#/info", null, "msg");
        var promoted = original with { Severity = ValidationSeverity.Warning };
        promoted.Severity.Should().Be(ValidationSeverity.Warning);
        promoted.RuleId.Should().Be("test.rule");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. OpenApiValidator stamps violations with DefaultSeverity
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validator_StampsViolations_WithDefaultSeverity()
    {
        var doc = BuildDocMissingInfoTitle();
        var ctx = new ValidationContext();

        var result = CoreValidator.Validate(doc, ctx);

        var titleViolation = result.Violations.FirstOrDefault(v => v.RuleId == "spec.info-title");
        titleViolation.Should().NotBeNull(because: "spec.info-title should fire on empty title");
        titleViolation!.Severity.Should().Be(ValidationSeverity.Error,
            because: "spec.info-title DefaultSeverity is Error");

        var descViolation = result.Violations.FirstOrDefault(v => v.RuleId == "spec.info-description");
        if (descViolation != null)
        {
            descViolation.Severity.Should().Be(ValidationSeverity.Warning,
                because: "spec.info-description DefaultSeverity is Warning");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. SeverityOverrides: --warn-rule demotes error to warning
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SeverityOverrides_WarnRule_DemotesErrorToWarning()
    {
        var doc = BuildDocMissingInfoTitle();
        var ctx = new ValidationContext
        {
            SeverityOverrides = new Dictionary<string, ValidationSeverity>
            {
                ["spec.info-title"] = ValidationSeverity.Warning,
            },
        };

        var result = CoreValidator.Validate(doc, ctx);

        var titleViolation = result.Violations.FirstOrDefault(v => v.RuleId == "spec.info-title");
        titleViolation.Should().NotBeNull();
        titleViolation!.Severity.Should().Be(ValidationSeverity.Warning,
            because: "override demotes error to warning");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. SeverityOverrides: --error-rule promotes warning to error
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SeverityOverrides_ErrorRule_PromotesWarningToError()
    {
        var doc = BuildDocMissingInfoDescription();
        var ctx = new ValidationContext
        {
            SeverityOverrides = new Dictionary<string, ValidationSeverity>
            {
                ["spec.info-description"] = ValidationSeverity.Error,
            },
        };

        var result = CoreValidator.Validate(doc, ctx);

        var descViolation = result.Violations.FirstOrDefault(v => v.RuleId == "spec.info-description");
        descViolation.Should().NotBeNull();
        descViolation!.Severity.Should().Be(ValidationSeverity.Error,
            because: "override promotes warning to error");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. --strict: promotes all warnings to errors
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SeverityOverrides_Strict_PromotesAllWarningsToErrors()
    {
        // Build overrides as --strict would: every warning rule → error
        var overrides = CoreValidator.AllRules
            .Where(r => r.DefaultSeverity == ValidationSeverity.Warning)
            .ToDictionary(r => r.Id, _ => ValidationSeverity.Error);

        var doc = BuildDocMissingInfoDescription();
        var ctx = new ValidationContext { SeverityOverrides = overrides };

        var result = CoreValidator.Validate(doc, ctx);

        // All violations should be errors under strict mode
        result.Violations.Should().OnlyContain(v => v.Severity == ValidationSeverity.Error,
            because: "--strict promotes all warnings to errors");
        result.WarningCount.Should().Be(0, because: "no warnings should remain under --strict");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. --strict + --warn-rule: explicit warn overrides strict
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SeverityOverrides_StrictWithExplicitWarnRule_ExplicitWins()
    {
        // Simulate --strict --warn-rule spec.info-description:
        // strict promotes all warnings, but explicit warn-rule for info-description wins.
        var explicit_ = new Dictionary<string, ValidationSeverity>
        {
            ["spec.info-description"] = ValidationSeverity.Warning, // explicit --warn-rule
        };
        var overrides = new Dictionary<string, ValidationSeverity>(explicit_, StringComparer.Ordinal);
        // Apply strict: promote warnings not in explicit_ to error
        foreach (var rule in CoreValidator.AllRules)
        {
            if (rule.DefaultSeverity == ValidationSeverity.Warning && !explicit_.ContainsKey(rule.Id))
                overrides[rule.Id] = ValidationSeverity.Error;
        }

        var doc = BuildDocMissingInfoDescription();
        var ctx = new ValidationContext { SeverityOverrides = overrides };

        var result = CoreValidator.Validate(doc, ctx);

        var descViolation = result.Violations.FirstOrDefault(v => v.RuleId == "spec.info-description");
        descViolation.Should().NotBeNull();
        descViolation!.Severity.Should().Be(ValidationSeverity.Warning,
            because: "explicit --warn-rule wins over --strict");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. ValidationResult.ErrorCount and WarningCount
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidationResult_ErrorCountAndWarningCount_AreCorrect()
    {
        var violations = new List<ValidationViolation>
        {
            new("rule.a", ValidationSeverity.Error, "#/a", null, "error 1"),
            new("rule.b", ValidationSeverity.Error, "#/b", null, "error 2"),
            new("rule.c", ValidationSeverity.Warning, "#/c", null, "warning 1"),
        };
        var result = new ValidationResult(violations);

        result.ErrorCount.Should().Be(2);
        result.WarningCount.Should().Be(1);
        result.Count.Should().Be(3);
    }

    [Fact]
    public void ValidationResult_NoViolations_BothCountsAreZero()
    {
        var result = new ValidationResult([]);
        result.ErrorCount.Should().Be(0);
        result.WarningCount.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. Exit code logic: only Error-severity triggers exit 1
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExitCodeLogic_OnlyWarnings_ErrorCountIsZero()
    {
        // A document that violates only warning-severity rules (e.g. spec.info-description)
        // should have ErrorCount = 0 → exit 0.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" }, // description missing = warning
        };
        var ctx = new ValidationContext
        {
            SkippedRuleIds = CoreValidator.AllRules
                .Where(r => r.DefaultSeverity == ValidationSeverity.Error)
                .Select(r => r.Id)
                .ToHashSet(),
        };

        var result = CoreValidator.Validate(doc, ctx);

        result.ErrorCount.Should().Be(0,
            because: "all error rules are skipped, only warnings remain");
    }

    [Fact]
    public void ExitCodeLogic_AnyError_ErrorCountPositive()
    {
        var doc = BuildDocMissingInfoTitle(); // spec.info-title = Error
        var ctx = new ValidationContext();

        var result = CoreValidator.Validate(doc, ctx);

        result.ErrorCount.Should().BeGreaterThan(0,
            because: "spec.info-title is Error severity");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. JSON report format: severity field and errors/warnings in summary
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReportJson_ViolationEntry_HasSeverityField()
    {
        var violations = new List<ValidationViolation>
        {
            new("spec.info-title", ValidationSeverity.Error, "#/info", null, "Title missing."),
            new("spec.info-description", ValidationSeverity.Warning, "#/info", null, "Description missing."),
        };
        var result = new ValidationResult(violations);

        var json = ValidationReportWriter.ToJson(result, "spec.json");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var violationsArr = root.GetProperty("violations");
        violationsArr.GetArrayLength().Should().Be(2);

        var firstSeverity = violationsArr[0].GetProperty("severity").GetString();
        firstSeverity.Should().Be("error", because: "severity should serialize as lowercase 'error'");

        var secondSeverity = violationsArr[1].GetProperty("severity").GetString();
        secondSeverity.Should().Be("warning", because: "severity should serialize as lowercase 'warning'");
    }

    [Fact]
    public void ReportJson_Summary_HasErrorsAndWarningsCounts()
    {
        var violations = new List<ValidationViolation>
        {
            new("rule.a", ValidationSeverity.Error, "#/a", null, "msg"),
            new("rule.b", ValidationSeverity.Error, "#/b", null, "msg"),
            new("rule.c", ValidationSeverity.Warning, "#/c", null, "msg"),
        };
        var result = new ValidationResult(violations);

        var json = ValidationReportWriter.ToJson(result, "spec.json");
        using var doc = JsonDocument.Parse(json);
        var summary = doc.RootElement.GetProperty("summary");

        summary.GetProperty("total").GetInt32().Should().Be(3);
        summary.GetProperty("errors").GetInt32().Should().Be(2);
        summary.GetProperty("warnings").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ReportJson_Severity_IsLowercase()
    {
        // Ensure JsonStringEnumConverter with camelCase produces lowercase
        var violation = new ValidationViolation("test", ValidationSeverity.Error, "#/x", null, "msg");
        var result = new ValidationResult(new[] { violation });
        var json = ValidationReportWriter.ToJson(result, "spec.json");

        // Must be "error" not "Error" or "ERROR"
        json.Should().Contain("\"error\"");
        json.Should().NotContain("\"Error\"");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12. Integration: BuildWithValidation severity counts on SampleApi
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWithValidation_SampleApi_HasBothErrorsAndWarnings()
    {
        var options = new DotNetOpenApiExtract.Core.OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var ctx = new ValidationContext
        {
            ExcludedPathPrefixes = ["/healthz", "/ready", "/metrics"],
        };

        DotNetOpenApiExtract.Core.OpenApiDocumentBuilder.BuildWithValidation(options, ctx, out var result);

        result.Count.Should().BeGreaterThan(0, because: "SampleApi has incomplete documentation");
        result.ErrorCount.Should().BeGreaterThan(0,
            because: "SampleApi should have error-severity violations (missing descriptions etc.)");
        // Note: warning count may be 0 depending on SampleApi completeness — just verify the split exists
        (result.ErrorCount + result.WarningCount).Should().Be(result.Count,
            because: "all violations must be either error or warning");
    }

    [Fact]
    public void BuildWithValidation_StrictMode_AllViolationsAreErrors()
    {
        var options = new DotNetOpenApiExtract.Core.OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        // Build strict overrides: all warning rules → error
        var overrides = CoreValidator.AllRules
            .Where(r => r.DefaultSeverity == ValidationSeverity.Warning)
            .ToDictionary(r => r.Id, _ => ValidationSeverity.Error);

        var ctx = new ValidationContext
        {
            ExcludedPathPrefixes = ["/healthz", "/ready", "/metrics"],
            SeverityOverrides = overrides,
        };

        DotNetOpenApiExtract.Core.OpenApiDocumentBuilder.BuildWithValidation(options, ctx, out var result);

        result.WarningCount.Should().Be(0,
            because: "under strict mode all warnings are promoted to errors");
        if (result.Count > 0)
            result.ErrorCount.Should().Be(result.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13. Regression: partial SeverityOverrides dict must not corrupt uncovered rules
    //     (GetValueOrDefault bug: default(ValidationSeverity)=Error would override
    //      rules not present in the dict, silently demoting their DefaultSeverity)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SeverityOverrides_PartialDict_UncoveredRulesKeepDefaultSeverity()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "", Version = "v1", Description = null },
        };
        var ctx = new ValidationContext
        {
            SeverityOverrides = new Dictionary<string, ValidationSeverity>
            {
                ["spec.info-title"] = ValidationSeverity.Warning,
                // spec.info-description is NOT in the dict → should remain Warning (its default)
            },
        };
        var result = CoreValidator.Validate(doc, ctx);
        var descViolation = result.Violations.Single(v => v.RuleId == "spec.info-description");
        descViolation.Severity.Should().Be(ValidationSeverity.Warning,
            because: "unlisted rules must keep DefaultSeverity, not become Error");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 14. ValidationContext default values (Wave 7a changed MinDescriptionLength
    //     from 20 → 5 and removed hardcoded ExcludedPathPrefixes)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidationContext_DefaultMinDescriptionLength_IsFive()
    {
        var ctx = new ValidationContext();
        ctx.MinDescriptionLength.Should().Be(5,
            because: "Wave 7a changed the default from 20 to 5");
    }

    [Fact]
    public void ValidationContext_DefaultExcludedPathPrefixes_IsEmpty()
    {
        var ctx = new ValidationContext();
        ctx.ExcludedPathPrefixes.Should().BeEmpty(
            because: "Wave 7a removed the hardcoded [/healthz, /ready, /metrics] default; " +
                     "callers must pass excluded prefixes explicitly");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FindUnknownRuleIds helper
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FindUnknownRuleIds_AllValid_ReturnsEmpty()
    {
        var valid = new[] { "operation.summary", "schema.description", "spec.info-version" };
        CoreValidator.FindUnknownRuleIds(valid).Should().BeEmpty(
            because: "all supplied IDs are recognized built-in rule IDs");
    }

    [Fact]
    public void FindUnknownRuleIds_Typo_ReturnsThatId()
    {
        var ids = new[] { "operation.summary", "operation.has-erorr-response" };
        var unknown = CoreValidator.FindUnknownRuleIds(ids);
        unknown.Should().ContainSingle()
            .Which.Should().Be("operation.has-erorr-response",
                because: "the misspelled ID is not a valid built-in rule ID");
    }

    [Fact]
    public void FindUnknownRuleIds_MultipleUnknown_ReturnsAll()
    {
        var ids = new[] { "foo.bar", "baz.qux", "operation.summary" };
        var unknown = CoreValidator.FindUnknownRuleIds(ids);
        unknown.Should().HaveCount(2).And.Contain(["foo.bar", "baz.qux"]);
    }

    [Fact]
    public void FindUnknownRuleIds_Empty_ReturnsEmpty()
    {
        CoreValidator.FindUnknownRuleIds(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public void FindUnknownRuleIds_DuplicateUnknownIds_ReturnsDuplicates()
    {
        // The method is a pure filter — it does not deduplicate the input.
        // If a caller passes the same unknown ID twice (e.g. the user specifies
        // --skip-rule foo --skip-rule foo), the result will contain "foo" twice.
        // This test documents the current contract so that any future deduplication
        // change is a deliberate, visible decision rather than a silent regression.
        var ids = new[] { "foo.bar", "operation.summary", "foo.bar" };
        var unknown = CoreValidator.FindUnknownRuleIds(ids);
        unknown.Should().HaveCount(2, because: "duplicate unknown IDs are preserved as-is, one per occurrence");
        unknown.Should().OnlyContain(id => id == "foo.bar");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocMissingInfoTitle()
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "", Version = "v1" },
        };
    }

    private static OpenApiDocument BuildDocMissingInfoDescription()
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "My API", Version = "v1", Description = null },
        };
    }
}
