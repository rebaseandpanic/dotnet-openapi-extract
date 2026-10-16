using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Orchestrates validation of an <see cref="OpenApiDocument"/> using the registered set of rules.
/// </summary>
public sealed class OpenApiValidator
{
    /// <summary>
    /// All built-in validation rules in canonical order.
    /// Spec-MUST violations (error severity).
    /// Documentation and consistency of operations, parameters, responses, schemas and security.
    /// Structural completeness (warning severity).
    /// Developer experience (warning severity, off by default — see <see cref="DefaultOffRuleIds"/>).
    /// API conventions (mixed severity, some off by default — see <see cref="DefaultOffRuleIds"/>).
    /// Document structure of OpenAPI 3.1/3.2 (error, on by default; each rule applies to the
    /// versions that have the checked field).
    /// </summary>
    public static IReadOnlyList<IValidationRule> AllRules { get; } = new IValidationRule[]
    {
        // ── Spec-MUST violations (error) ──────────────────────────────────────
        new SpecNoRefSiblingsRule(),
        new SpecInfoVersionRule(),
        new OperationOperationIdUniqueRule(),
        new PathParamsMatchRule(),
        new PathNoEmptyDeclarationRule(),
        new ParameterPathRequiredTrueRule(),
        new SchemaArrayItemsRule(),
        new OperationParametersUniqueRule(),

        // ── Documentation and consistency ─────────────────────────────────────
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

        // ── Structural completeness (warning) ─────────────────────────────────
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

        // ── Developer experience (warning, off by default) ────────────────────
        new SpecServersDefinedRule(),
        new TagDescriptionRule(),
        new ComponentNoUnusedRule(),
        new SpecNoEvalInMarkdownRule(),
        new SpecNoScriptTagsInMarkdownRule(),

        // ── API conventions ───────────────────────────────────────────────────
        new OperationHasRequiredResponseCodesRule(), // off by default, error
        new OperationRequestBodyDescriptionRule(),   // warning, on by default
        new OperationOperationIdPascalCaseRule(),    // off by default, warning
        new SchemaAdditionalPropertiesExplicitRule(), // off by default, warning
        new ResponseContentTypeJsonDefaultRule(),    // off by default, warning

        // ── Document structure of OpenAPI 3.1/3.2 (error, on by default) ──────
        new SpecLicenseIdentifierOrUrlRule(),        // 3.1+
        new SpecPathsOrWebhooksOrComponentsRule(),   // 3.0: paths; 3.1+: one of the three
        new TagParentDefinedRule(),                  // 3.2
        new TagNoParentCycleRule(),                  // 3.2
        new SpecServerNamesUniqueRule(),             // 3.2
        new DiscriminatorDefaultMappingWhenOptionalRule(), // 3.2
    };

    /// <summary>
    /// The IDs of all built-in rules, in canonical order.
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
    /// Rule IDs that are <b>off by default</b> (the developer experience rules and selected API conventions).
    /// These rules run only when explicitly enabled via <see cref="ValidationContext.EnabledRuleIds"/>
    /// or the <c>--enable-rule</c> CLI flag. They are not affected by <c>--strict</c>.
    /// </summary>
    public static IReadOnlyList<string> DefaultOffRuleIds { get; } = new[]
    {
        // Developer experience
        "spec.servers-defined",
        "tag.description",
        "component.no-unused",
        "spec.no-eval-in-markdown",
        "spec.no-script-tags-in-markdown",
        // API conventions (off by default)
        "operation.has-required-response-codes",
        "operation.operation-id-pascal-case",
        "schema.additional-properties-explicit",
        "response.content-type-json-default",
    };

    /// <summary>
    /// Validates <paramref name="document"/> against all rules (excluding any listed in
    /// <see cref="ValidationContext.SkippedRuleIds"/> and off-by-default rules not in
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

            // Off-by-default rules run only when explicitly enabled
            if (DefaultOffRuleIds.Contains(rule.Id) && !context.EnabledRuleIds.Contains(rule.Id))
                continue;

            // Determine effective severity: explicit override wins over the rule's default for the
            // document's version. Use TryGetValue to avoid GetValueOrDefault returning
            // default(ValidationSeverity)=Error for rules not present in the dict, which would
            // silently override their default.
            var effectiveSeverity = rule.GetDefaultSeverity(context.OpenApiSpecVersion);
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
