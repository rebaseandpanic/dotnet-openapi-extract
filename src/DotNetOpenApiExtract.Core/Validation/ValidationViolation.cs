namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Represents a single OpenAPI spec violation produced by a <see cref="IValidationRule"/>.
/// </summary>
/// <param name="RuleId">The ID of the rule that produced this violation (e.g. "operation.summary").</param>
/// <param name="Severity">The effective severity of this violation, as determined by the validator.</param>
/// <param name="JsonPointer">RFC 6901 JSON Pointer identifying the violating element (e.g. "#/paths/~1api~1users/get").</param>
/// <param name="Location">Source location metadata, or <see langword="null"/> when not available.</param>
/// <param name="Message">Human-readable one-liner describing the violation.</param>
public sealed record ValidationViolation(
    string RuleId,
    ValidationSeverity Severity,
    string JsonPointer,
    ViolationLocation? Location,
    string Message);

/// <summary>
/// Source location coordinates for a <see cref="ValidationViolation"/>.
/// All fields are best-effort — null when the information cannot be determined.
/// </summary>
/// <param name="ClassName">CLR class name (e.g. "UsersController").</param>
/// <param name="MethodName">CLR method name, for operation-level violations.</param>
/// <param name="PropertyName">CLR property name, for schema property violations.</param>
/// <param name="File">Absolute or relative source file path, when source analysis is available.</param>
/// <param name="Line">1-based line number in <see cref="File"/>, when source analysis is available.</param>
public sealed record ViolationLocation(
    string? ClassName,
    string? MethodName,
    string? PropertyName,
    string? File,
    int? Line);
