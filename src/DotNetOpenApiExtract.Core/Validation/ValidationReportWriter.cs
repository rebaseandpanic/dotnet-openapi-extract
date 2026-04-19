using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Serializes <see cref="ValidationResult"/> instances to JSON report format.
/// </summary>
public static class ValidationReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Serializes a <see cref="ValidationResult"/> to the standard JSON report format.
    /// </summary>
    /// <param name="result">The validation result to serialize.</param>
    /// <param name="specPath">The path of the OpenAPI spec file being validated.</param>
    /// <returns>A formatted JSON string.</returns>
    public static string ToJson(ValidationResult result, string specPath)
    {
        var report = new ValidationReport
        {
            Spec = specPath,
            Violations = result.Violations.Select(v => new ViolationEntry
            {
                Rule = v.RuleId,
                Severity = v.Severity,
                JsonPointer = v.JsonPointer,
                Location = v.Location != null ? new LocationEntry
                {
                    ClassName = v.Location.ClassName,
                    MethodName = v.Location.MethodName,
                    PropertyName = v.Location.PropertyName,
                    File = v.Location.File,
                    Line = v.Location.Line,
                } : null,
                Message = v.Message,
            }).ToList(),
            Summary = new SummaryEntry
            {
                Total = result.Count,
                Errors = result.ErrorCount,
                Warnings = result.WarningCount,
                ByRule = result.ByRule,
                SkippedRules = result.SkippedRules.ToList(),
            },
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    /// <summary>
    /// Writes a brief human-readable summary to <paramref name="writer"/>.
    /// </summary>
    public static void WriteSummary(ValidationResult result, TextWriter writer)
    {
        var skippedCount = result.SkippedRules.Count;
        var skippedSuffix = skippedCount > 0 ? $" ({skippedCount} rule{(skippedCount == 1 ? "" : "s")} skipped due to errors)" : "";
        writer.WriteLine($"Validation: {result.Count} violation{(result.Count == 1 ? "" : "s")} " +
                         $"({result.ErrorCount} error{(result.ErrorCount == 1 ? "" : "s")}, " +
                         $"{result.WarningCount} warning{(result.WarningCount == 1 ? "" : "s")}){skippedSuffix}");
        foreach (var (ruleId, count) in result.ByRule.OrderBy(kv => kv.Key))
            writer.WriteLine($"  {ruleId}: {count}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Report DTO types (private — not part of public API)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class ValidationReport
    {
        public string Spec { get; set; } = "";
        public List<ViolationEntry> Violations { get; set; } = new();
        public SummaryEntry Summary { get; set; } = new();
    }

    private sealed class ViolationEntry
    {
        public string Rule { get; set; } = "";
        public ValidationSeverity Severity { get; set; }
        public string JsonPointer { get; set; } = "";
        public LocationEntry? Location { get; set; }
        public string Message { get; set; } = "";
    }

    private sealed class LocationEntry
    {
        public string? ClassName { get; set; }
        public string? MethodName { get; set; }
        public string? PropertyName { get; set; }
        public string? File { get; set; }
        public int? Line { get; set; }
    }

    private sealed class SummaryEntry
    {
        public int Total { get; set; }
        public int Errors { get; set; }
        public int Warnings { get; set; }
        public IReadOnlyDictionary<string, int> ByRule { get; set; } = new Dictionary<string, int>();
        public List<string> SkippedRules { get; set; } = new();
    }
}
