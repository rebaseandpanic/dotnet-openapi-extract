namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Severity level of a <see cref="ValidationViolation"/>.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Blocks CI (exit code 1). Broken spec or clearly wrong behavior.</summary>
    Error,

    /// <summary>Reported but does not fail the build by default. Best-practice or opinionated rule.</summary>
    Warning,
}
