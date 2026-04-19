namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// The result of running <see cref="OpenApiValidator.Validate"/> against an OpenAPI document.
/// </summary>
/// <remarks>
/// This type is not thread-safe: the <see cref="ErrorCount"/>, <see cref="WarningCount"/>,
/// and <see cref="ByRule"/> properties use lazy caching that does not synchronize.
/// Intended for single-threaded consumption within a single validation pass.
/// </remarks>
/// <param name="Violations">
/// All violations found, sorted by (RuleId, JsonPointer) for deterministic output.
/// </param>
public sealed record ValidationResult(IReadOnlyList<ValidationViolation> Violations)
{
    /// <summary>Total number of violations.</summary>
    public int Count => Violations.Count;

    /// <summary>Number of Error-severity violations. Computed once and cached.</summary>
    private int? _errorCount;
    public int ErrorCount => _errorCount ??= Violations.Count(v => v.Severity == ValidationSeverity.Error);

    /// <summary>Number of Warning-severity violations. Computed once and cached.</summary>
    private int? _warningCount;
    public int WarningCount => _warningCount ??= Violations.Count(v => v.Severity == ValidationSeverity.Warning);

    /// <summary>Violations grouped by rule ID with their counts. Computed once and cached.</summary>
    private IReadOnlyDictionary<string, int>? _byRule;
    public IReadOnlyDictionary<string, int> ByRule =>
        _byRule ??= Violations
            .GroupBy(v => v.RuleId)
            .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Rule IDs that were skipped due to an unhandled exception during execution.
    /// Empty when all rules ran successfully.
    /// </summary>
    public IReadOnlyList<string> SkippedRules { get; init; } = Array.Empty<string>();
}
