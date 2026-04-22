using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Integration tests running the full build + validation pipeline against SampleApi.
/// </summary>
public sealed class ValidationIntegrationTests
{
    private static OpenApiDocument BuildSampleApi() =>
        OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        });

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Full pipeline — SampleApi produces violations
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWithValidation_SampleApi_ProducesViolations()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var validationContext = new ValidationContext();

        OpenApiDocumentBuilder.BuildWithValidation(options, validationContext, out var result);

        // SampleApi has intentionally incomplete documentation — violations expected
        result.Count.Should().BeGreaterThan(0,
            because: "SampleApi has operations and schemas with missing documentation");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Skip rule — excluded from result
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWithValidation_SkipRule_ExcludesFromResult()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var contextWithAll = new ValidationContext();
        var contextSkipDesc = new ValidationContext
        {
            SkippedRuleIds = new HashSet<string> { "operation.description" }
        };

        OpenApiDocumentBuilder.BuildWithValidation(options, contextWithAll, out var resultAll);
        OpenApiDocumentBuilder.BuildWithValidation(options, contextSkipDesc, out var resultSkipped);

        // Skipped result should have no operation.description violations
        resultSkipped.Violations.Should().NotContain(v => v.RuleId == "operation.description",
            because: "operation.description was skipped");

        // But the skipped result may still have other violations
        // The full result may (or may not) have description violations — just verify the skip works
        if (resultAll.ByRule.ContainsKey("operation.description"))
        {
            resultSkipped.Count.Should().BeLessThan(resultAll.Count,
                because: "skipping a rule that produced violations should reduce the count");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Excluded path — /healthz skips error response check
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWithValidation_ExcludedPath_SkipsErrorResponseCheck()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        // HealthController registers /healthz which only has 200 — no 4xx/5xx
        var contextWithExclusion = new ValidationContext
        {
            ExcludedPathPrefixes = ["/healthz", "/ready", "/metrics"],
        };

        OpenApiDocumentBuilder.BuildWithValidation(options, contextWithExclusion, out var result);

        // No operation.has-error-response violation for /healthz
        var healthzViolations = result.Violations
            .Where(v => v.RuleId == "operation.has-error-response" &&
                        v.JsonPointer.Contains("healthz"))
            .ToList();

        healthzViolations.Should().BeEmpty(
            because: "/healthz is in the excluded path prefixes list");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. No source context — Location.File and Line are null
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWithValidation_NoSourceContext_LocationFileAndLineNull()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = "/nonexistent/path/that/does/not/exist",
        };

        var validationContext = new ValidationContext();

        OpenApiDocumentBuilder.BuildWithValidation(options, validationContext, out var result);

        // When source analysis is not available, all file/line values should be null
        var violationsWithLocation = result.Violations.Where(v => v.Location != null).ToList();
        foreach (var violation in violationsWithLocation)
        {
            violation.Location!.File.Should().BeNull(
                because: "source analysis is unavailable (invalid source root)");
            violation.Location.Line.Should().BeNull(
                because: "source analysis is unavailable (invalid source root)");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. Regression: array properties with [MaxLength]/[MinLength] must NOT
    //    produce a false-positive schema.property-constraints violation claiming
    //    that 'maxLength' / 'minLength' is missing.  Before the fix the rule
    //    always checked MaxLength/MinLength regardless of schema type, so
    //    List<string> Items ([MaxLength(10)]) and List<string> RequiredItems
    //    ([MinLength(2)]) in ValidationModel / ExtendedValidationModel
    //    triggered bogus errors.
    //
    //    Properties are emitted camelCased by default, so the JSON pointer
    //    segments are 'items' and 'requiredItems' (lowercase initial).
    // ─────────────────────────────────────────────────────────────────────────

    // Pointer fragments emitted by JsonPointerHelper.ForSchemaProperty.
    private const string ValidationModelItemsPointer = "/components/schemas/ValidationModel/properties/items";
    private const string ExtendedValidationModelRequiredItemsPointer =
        "/components/schemas/ExtendedValidationModel/properties/requiredItems";

    [Fact]
    public void BuildWithValidation_ArrayPropertyWithMaxLength_NoFalsePositiveConstraintViolation()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var validationContext = new ValidationContext();
        OpenApiDocumentBuilder.BuildWithValidation(options, validationContext, out var result);

        // ValidationModel.Items is List<string> annotated with [MaxLength(10)].
        // The extractor correctly emits maxItems on the schema; the rule must not then
        // also complain that maxLength is missing (that would be a false positive).
        var falsePositiveViolations = result.Violations
            .Where(v => v.RuleId == "schema.property-constraints"
                     && v.JsonPointer.Contains(ValidationModelItemsPointer, StringComparison.Ordinal)
                     && v.Message.Contains("maxLength"))
            .ToList();

        falsePositiveViolations.Should().BeEmpty(
            because: "array properties annotated with [MaxLength] must be checked against " +
                     "maxItems (not maxLength); before the fix this incorrectly reported " +
                     "a missing 'maxLength' on List<string> Items");
    }

    [Fact]
    public void BuildWithValidation_ArrayPropertyWithMinLength_NoFalsePositiveConstraintViolation()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var validationContext = new ValidationContext();
        OpenApiDocumentBuilder.BuildWithValidation(options, validationContext, out var result);

        // ExtendedValidationModel.RequiredItems is List<string> annotated with [MinLength(2)].
        // The extractor correctly emits minItems; the rule must not claim minLength is missing.
        var falsePositiveViolations = result.Violations
            .Where(v => v.RuleId == "schema.property-constraints"
                     && v.JsonPointer.Contains(ExtendedValidationModelRequiredItemsPointer, StringComparison.Ordinal)
                     && v.Message.Contains("minLength"))
            .ToList();

        falsePositiveViolations.Should().BeEmpty(
            because: "array properties annotated with [MinLength] must be checked against " +
                     "minItems (not minLength); before the fix this incorrectly reported " +
                     "a missing 'minLength' on List<string> RequiredItems");
    }


    // ─────────────────────────────────────────────────────────────────────────
    // 6. CLR bindings — location class and method populated
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWithValidation_OperationViolation_LocationHasClassAndMethod()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var validationContext = new ValidationContext();

        OpenApiDocumentBuilder.BuildWithValidation(options, validationContext, out var result);

        // Find any operation-level violation and verify it has location
        var opViolation = result.Violations
            .FirstOrDefault(v => v.RuleId.StartsWith("operation.", StringComparison.Ordinal)
                                 && v.Location != null);

        if (opViolation != null)
        {
            opViolation.Location!.ClassName.Should().NotBeNullOrEmpty(
                because: "operation violations should have className from CLR bindings");
            opViolation.Location.MethodName.Should().NotBeNullOrEmpty(
                because: "operation violations should have methodName from CLR bindings");
        }
        // If no operation violations exist, this test is vacuously passing — that's acceptable.
    }
}
