using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Carries user options and CLR-to-schema bindings used by validation rules.
/// Pass an instance of this class to <see cref="OpenApiValidator.Validate"/>.
/// </summary>
public sealed class ValidationContext
{
    /// <summary>
    /// Minimum length (in characters) required for description fields.
    /// Applied by rules such as <c>operation.description</c> and <c>schema.description</c>.
    /// Default: 5.
    /// </summary>
    public int MinDescriptionLength { get; init; } = 5;

    /// <summary>
    /// Path prefixes that are skipped for rules requiring error responses (e.g. health check endpoints).
    /// Comparisons are case-insensitive.
    /// Default: empty (no paths excluded by default — pass /healthz, /ready, /metrics explicitly if needed).
    /// </summary>
    public IReadOnlyList<string> ExcludedPathPrefixes { get; init; } = [];

    /// <summary>
    /// Rule IDs that should be completely skipped during validation.
    /// </summary>
    public IReadOnlySet<string> SkippedRuleIds { get; init; } = new HashSet<string>();

    /// <summary>
    /// Rule IDs that are explicitly enabled, overriding the <see cref="OpenApiValidator.DefaultOffRuleIds"/>
    /// opt-in list. Rules in <c>DefaultOffRuleIds</c> that appear here will run even though they are off
    /// by default. Corresponds to the <c>--enable-rule</c> CLI flag.
    /// </summary>
    public IReadOnlySet<string> EnabledRuleIds { get; init; } = new HashSet<string>();

    /// <summary>
    /// Per-rule severity overrides. When a rule ID is present in this dictionary, the specified
    /// severity is used instead of the rule's <see cref="IValidationRule.DefaultSeverity"/>.
    /// Supports demoting errors to warnings (<c>--warn-rule</c>) and promoting warnings to errors
    /// (<c>--error-rule</c>, <c>--strict</c>).
    /// <para>Null or empty = use each rule's default severity.</para>
    /// </summary>
    public IReadOnlyDictionary<string, ValidationSeverity>? SeverityOverrides { get; init; }

    /// <summary>
    /// Map from operation key (<c>"METHOD /path"</c>, e.g. <c>"GET /api/users"</c>) to
    /// the controller and action that produced the operation.
    /// Used to resolve <see cref="ViolationLocation.ClassName"/> and <see cref="ViolationLocation.MethodName"/>.
    /// <para>Null in standalone mode (no assembly available).</para>
    /// </summary>
    public IReadOnlyDictionary<string, (ControllerInfo Controller, ActionInfo Action)>? ActionByOperationKey { get; init; }

    /// <summary>
    /// Map from schema component ID (e.g. <c>"UserDto"</c>) to the CLR <see cref="Type"/>
    /// that produced the schema. Used to resolve <see cref="ViolationLocation.ClassName"/>
    /// and <see cref="ViolationLocation.PropertyName"/>.
    /// <para>Null in standalone mode (no assembly available).</para>
    /// </summary>
    public IReadOnlyDictionary<string, Type>? TypeBySchemaId { get; init; }

    /// <summary>
    /// Roslyn source analysis context. When <see cref="SourceAnalysisContext.IsAvailable"/> is
    /// <see langword="true"/>, <see cref="ViolationLocation.File"/> and <see cref="ViolationLocation.Line"/>
    /// can be resolved.
    /// <para>Null in standalone mode.</para>
    /// </summary>
    public SourceAnalysisContext? SourceContext { get; init; }

    /// <summary>
    /// The OpenAPI spec version the document targets. Used by version-conditional rules
    /// (e.g., <c>spec.no-ref-siblings</c>). Null means version is unknown; version-specific rules
    /// default to applying conservatively (emit violations) so that the user can suppress via
    /// <c>--skip-rule</c> if they know they are targeting 3.1+.
    /// </summary>
    public OpenApiSpecVersion? OpenApiSpecVersion { get; init; }

    /// <summary>
    /// Required HTTP response codes per HTTP method filter, used by the
    /// <c>operation.has-required-response-codes</c> rule (R48).
    /// Each entry specifies a method filter (e.g. <c>"POST"</c>, <c>"mutating"</c>, <c>"*"</c>)
    /// and a required HTTP status code (e.g. 422, 401).
    /// When null or empty, R48 emits no violations.
    /// </summary>
    public IReadOnlyList<(string MethodFilter, int Code)>? RequiredResponseCodes { get; init; }

    /// <summary>
    /// Per-rule overrides for the minimum description length.
    /// When a rule ID is present, that value is used instead of <see cref="MinDescriptionLength"/>.
    /// Corresponds to the <c>--rule-min-length</c> CLI flag.
    /// <para>Null or empty = use <see cref="MinDescriptionLength"/> for all rules.</para>
    /// </summary>
    public IReadOnlyDictionary<string, int>? MinDescriptionLengthPerRule { get; init; }

    /// <summary>
    /// Returns the effective minimum description length for the given rule.
    /// If <paramref name="ruleId"/> has an entry in <see cref="MinDescriptionLengthPerRule"/>,
    /// that value is returned; otherwise, <see cref="MinDescriptionLength"/> is returned.
    /// </summary>
    /// <param name="ruleId">The rule ID to look up.</param>
    public int GetMinDescriptionLength(string ruleId)
    {
        if (MinDescriptionLengthPerRule != null &&
            MinDescriptionLengthPerRule.TryGetValue(ruleId, out var perRuleValue))
            return perRuleValue;
        return MinDescriptionLength;
    }
}
