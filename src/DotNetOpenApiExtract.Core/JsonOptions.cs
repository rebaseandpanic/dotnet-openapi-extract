namespace DotNetOpenApiExtract.Core;

/// <summary>
/// JSON property naming policy applied when serializing property names to the OpenAPI schema.
/// </summary>
/// <remarks>
/// Maps to the runtime <c>System.Text.Json.JsonNamingPolicy</c> values plus <c>Preserve</c>.
/// The enum is defined here (not using the runtime type) to avoid taking a runtime dependency
/// on <c>System.Text.Json</c> in the extraction pipeline.
/// </remarks>
public enum JsonNamingPolicy
{
    /// <summary>Preserve property names as declared in C# (PascalCase).</summary>
    Preserve,

    /// <summary>lowerCamelCase — default in ASP.NET Core.</summary>
    CamelCase,

    /// <summary>snake_case_lower</summary>
    SnakeCaseLower,

    /// <summary>SNAKE_CASE_UPPER</summary>
    SnakeCaseUpper,

    /// <summary>kebab-case-lower</summary>
    KebabCaseLower,

    /// <summary>KEBAB-CASE-UPPER</summary>
    KebabCaseUpper,
}

/// <summary>
/// Controls when a property is ignored during JSON serialization.
/// Mirrors <c>System.Text.Json.Serialization.JsonIgnoreCondition</c>.
/// </summary>
public enum JsonIgnoreCondition
{
    /// <summary>Never ignore.</summary>
    Never,

    /// <summary>Always ignore.</summary>
    Always,

    /// <summary>Ignore when the value is the default value for its type.</summary>
    WhenWritingDefault,

    /// <summary>Ignore when the value is null.</summary>
    WhenWritingNull,
}

/// <summary>
/// Controls how numbers are handled during JSON serialization.
/// Mirrors <c>System.Text.Json.Serialization.JsonNumberHandling</c>.
/// </summary>
[Flags]
public enum JsonNumberHandling
{
    /// <summary>Numbers are only read and written as JSON numbers.</summary>
    Strict = 0,

    /// <summary>Allow reading numbers from JSON strings (e.g. "42").</summary>
    AllowReadingFromString = 1,

    /// <summary>Write numbers as JSON strings.</summary>
    WriteAsString = 2,

    /// <summary>Allow reading and writing special floating-point values (Infinity, NaN).</summary>
    AllowNamedFloatingPointLiterals = 4,
}
