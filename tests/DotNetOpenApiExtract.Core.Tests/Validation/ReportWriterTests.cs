using System.Text.Json;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Tests for <see cref="ValidationReportWriter"/>.
/// </summary>
public sealed class ReportWriterTests
{
    [Fact]
    public void ToJson_IncludesExpectedFields()
    {
        var violations = new List<ValidationViolation>
        {
            new ValidationViolation(
                "operation.description",
                ValidationSeverity.Error,
                "#/paths/~1api~1users/get",
                new ViolationLocation("UsersController", "GetUsers", null, "src/Controllers/UsersController.cs", 42),
                "Operation description is missing or shorter than 5 characters (actual: 0)."
            ),
            new ValidationViolation(
                "spec.info-description",
                ValidationSeverity.Warning,
                "#/info",
                null,
                "Document info.description is missing or shorter than 5 characters (actual: 5)."
            ),
        };

        var result = new ValidationResult(violations);
        var json = ValidationReportWriter.ToJson(result, "openapi.json");

        // Parse back for structured assertions
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("spec").GetString().Should().Be("openapi.json");

        var violationsArr = root.GetProperty("violations");
        violationsArr.GetArrayLength().Should().Be(2);

        var first = violationsArr[0];
        first.GetProperty("rule").GetString().Should().Be("operation.description");
        first.GetProperty("jsonPointer").GetString().Should().Be("#/paths/~1api~1users/get");
        first.GetProperty("message").GetString().Should().Contain("5"); // min description length in message
        first.GetProperty("severity").GetString().Should().Be("error");

        var location = first.GetProperty("location");
        location.GetProperty("className").GetString().Should().Be("UsersController");
        location.GetProperty("methodName").GetString().Should().Be("GetUsers");
        location.GetProperty("file").GetString().Should().Contain("UsersController.cs");
        location.GetProperty("line").GetInt32().Should().Be(42);

        var second = violationsArr[1];
        // Location is null for second violation — with WhenWritingNull it is omitted from the JSON entirely.
        if (second.TryGetProperty("location", out var loc2))
            loc2.ValueKind.Should().Be(JsonValueKind.Null);

        var summary = root.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(2);

        var byRule = summary.GetProperty("byRule");
        byRule.GetProperty("operation.description").GetInt32().Should().Be(1);
        byRule.GetProperty("spec.info-description").GetInt32().Should().Be(1);
    }

    [Fact]
    public void ToJson_EmptyResult_ProducesValidJson()
    {
        var result = new ValidationResult([]);
        var json = ValidationReportWriter.ToJson(result, "swagger.json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("violations").GetArrayLength().Should().Be(0);
        root.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(0);
    }

    // Fix W1: SkippedRules must be serialized into summary.skippedRules in the JSON report
    [Fact]
    public void ToJson_WithSkippedRules_IncludesSkippedRulesInSummary()
    {
        var result = new ValidationResult([])
        {
            SkippedRules = new[] { "fake.rule", "another.rule" },
        };
        var json = ValidationReportWriter.ToJson(result, "spec.json");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var summary = root.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(0);

        var skipped = summary.GetProperty("skippedRules");
        skipped.ValueKind.Should().Be(JsonValueKind.Array);
        skipped.GetArrayLength().Should().Be(2);

        var ids = Enumerable.Range(0, skipped.GetArrayLength())
            .Select(i => skipped[i].GetString())
            .ToList();
        ids.Should().Contain("fake.rule");
        ids.Should().Contain("another.rule");
    }

    [Fact]
    public void ToJson_NoSkippedRules_SkippedRulesArrayIsEmpty()
    {
        // When all rules ran successfully, skippedRules should be an empty array (not omitted).
        var result = new ValidationResult([]);
        var json = ValidationReportWriter.ToJson(result, "spec.json");

        using var doc = JsonDocument.Parse(json);
        var summary = doc.RootElement.GetProperty("summary");

        // The field must be present and be an empty array
        summary.TryGetProperty("skippedRules", out var skipped).Should().BeTrue(
            because: "skippedRules must always be present in the summary");
        skipped.ValueKind.Should().Be(JsonValueKind.Array);
        skipped.GetArrayLength().Should().Be(0);
    }
}
