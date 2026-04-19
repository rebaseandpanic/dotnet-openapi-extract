using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Orchestrates validation of an <see cref="OpenApiDocument"/> using the registered set of rules.
/// </summary>
public sealed class OpenApiValidator
{
    /// <summary>
    /// All 47 built-in validation rules in canonical order.
    /// Group A: spec-MUST violations (error severity).
    /// Group B: structural completeness (warning severity).
    /// Group C: developer experience (warning severity, off by default — see <see cref="DefaultOffRuleIds"/>).
    /// </summary>
    public static IReadOnlyList<IValidationRule> AllRules { get; } = new IValidationRule[]
    {
        // ── Group A — Spec-MUST violations (error) ────────────────────────────
        new SpecNoRefSiblingsRule(),
        new SpecInfoVersionRule(),
        new OperationOperationIdUniqueRule(),
        new PathParamsMatchRule(),
        new PathNoEmptyDeclarationRule(),
        new ParameterPathRequiredTrueRule(),
        new SchemaArrayItemsRule(),
        new OperationParametersUniqueRule(),

        // ── Original rules ────────────────────────────────────────────────────
        // Operation-level rules
        new OperationSummaryRule(),
        new OperationOperationIdRule(),
        new OperationDescriptionRule(),
        new OperationTagsRule(),
        new OperationHasErrorResponseRule(),
        new OperationSecurityRule(),
        new OperationDeprecatedHasNoteRule(),

        // Parameter-level rules
        new ParameterDescriptionRule(),
        new ParameterSchemaTypeRule(),
        new ParameterOptionalHasDefaultRule(),

        // Response-level rules
        new ResponseDescriptionRule(),
        new ResponseSchemaWhenBodyRule(),

        // Schema-level rules
        new SchemaDescriptionRule(),
        new SchemaPropertyDescriptionRule(),
        new SchemaPropertyFormatRule(),
        new SchemaRequiredConsistencyRule(),
        new SchemaPropertyConstraintsRule(),
        new SchemaEnumFilledRule(),

        // Enum-level rules
        new EnumTypeDescriptionRule(),
        new EnumValueDescriptionRule(),

        // Security rules
        new SecuritySchemeDefinedRule(),
        new SecuritySchemeDescriptionRule(),

        // Spec-level rules
        new SpecInfoTitleRule(),
        new SpecInfoDescriptionRule(),

        // ── Group B — Structural completeness (warning) ───────────────────────
        new OperationSuccessResponseRule(),
        new OperationOperationIdUrlSafeRule(),
        new PathNoTrailingSlashRule(),
        new PathNoQueryStringRule(),
        new PathNoIdenticalRule(),
        new TagNoDuplicatesRule(),
        new OperationTagDefinedRule(),
        new SchemaTypedEnumRule(),
        new SchemaNoDuplicateEnumRule(),
        new SchemaNoRequiredUndefinedRule(),

        // ── Group C — Developer experience (warning, off by default) ──────────
        new SpecServersDefinedRule(),
        new TagDescriptionRule(),
        new ComponentNoUnusedRule(),
        new SpecNoEvalInMarkdownRule(),
        new SpecNoScriptTagsInMarkdownRule(),
    };

    /// <summary>
    /// The IDs of all 47 built-in rules, in canonical order.
    /// </summary>
    public static IReadOnlyList<string> AllRuleIds => AllRules.Select(r => r.Id).ToList();

    /// <summary>
    /// Returns the subset of <paramref name="ruleIds"/> that are not recognized built-in rule IDs.
    /// Used by the CLI to warn the user about typos in --skip-rule, --warn-rule, --error-rule, --enable-rule.
    /// </summary>
    /// <param name="ruleIds">The rule IDs to check.</param>
    /// <returns>A list of unrecognized IDs (empty if all IDs are valid).</returns>
    public static IReadOnlyList<string> FindUnknownRuleIds(IEnumerable<string> ruleIds)
    {
        var known = new HashSet<string>(AllRuleIds, StringComparer.Ordinal);
        return ruleIds.Where(id => !known.Contains(id)).ToList();
    }

    /// <summary>
    /// Rule IDs that are <b>off by default</b> (Group C — developer experience rules).
    /// These rules run only when explicitly enabled via <see cref="ValidationContext.EnabledRuleIds"/>
    /// or the <c>--enable-rule</c> CLI flag. They are not affected by <c>--strict</c>.
    /// </summary>
    public static IReadOnlyList<string> DefaultOffRuleIds { get; } = new[]
    {
        "spec.servers-defined",
        "tag.description",
        "component.no-unused",
        "spec.no-eval-in-markdown",
        "spec.no-script-tags-in-markdown",
    };

    /// <summary>
    /// Validates <paramref name="document"/> against all rules (excluding any listed in
    /// <see cref="ValidationContext.SkippedRuleIds"/> and Group C rules not in
    /// <see cref="ValidationContext.EnabledRuleIds"/>) and returns the result.
    /// </summary>
    /// <param name="document">The OpenAPI document to validate.</param>
    /// <param name="context">Validation options and CLR bindings.</param>
    /// <returns>
    /// A <see cref="ValidationResult"/> containing all violations, sorted by
    /// (<see cref="ValidationViolation.RuleId"/>, <see cref="ValidationViolation.JsonPointer"/>)
    /// for deterministic output.
    /// </returns>
    public static ValidationResult Validate(OpenApiDocument document, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var violations = new List<ValidationViolation>();
        var skipped = new List<string>();

        foreach (var rule in AllRules)
        {
            if (context.SkippedRuleIds.Contains(rule.Id))
                continue;

            // Group C rules are off by default unless explicitly enabled
            if (DefaultOffRuleIds.Contains(rule.Id) && !context.EnabledRuleIds.Contains(rule.Id))
                continue;

            // Determine effective severity: explicit override wins over rule default.
            // Use TryGetValue to avoid GetValueOrDefault returning default(ValidationSeverity)=Error
            // for rules not present in the dict, which would silently override their DefaultSeverity.
            var effectiveSeverity = rule.DefaultSeverity;
            if (context.SeverityOverrides != null &&
                context.SeverityOverrides.TryGetValue(rule.Id, out var overrideSev))
                effectiveSeverity = overrideSev;

            try
            {
                foreach (var v in rule.Validate(document, context))
                {
                    // Re-stamp each violation with the effective severity (rule may use its DefaultSeverity
                    // as a placeholder; the validator is the authority on effective severity).
                    violations.Add(v with { Severity = effectiveSeverity });
                }
            }
            catch
            {
                // Rules must not crash the pipeline — record skip and continue.
                skipped.Add(rule.Id);
            }
        }

        // Sort for deterministic output
        var sorted = violations
            .OrderBy(v => v.RuleId, StringComparer.Ordinal)
            .ThenBy(v => v.JsonPointer, StringComparer.Ordinal)
            .ToList();

        return new ValidationResult(sorted) { SkippedRules = skipped };
    }
}
