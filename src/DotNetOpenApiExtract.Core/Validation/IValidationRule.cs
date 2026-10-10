using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// Contract for a single OpenAPI completeness validation rule.
/// Each rule carries a stable <see cref="Id"/> used in reports and <c>--skip-rule</c> flags.
/// </summary>
public interface IValidationRule
{
    /// <summary>
    /// Stable, dot-separated rule identifier used in reports and <c>--skip-rule</c> flags.
    /// Format: <c>scope.kebab-name</c> (e.g. <c>"operation.summary"</c>).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The default severity for violations produced by this rule.
    /// The validator may override this via <see cref="ValidationContext.SeverityOverrides"/>.
    /// </summary>
    ValidationSeverity DefaultSeverity { get; }

    /// <summary>
    /// The default severity of this rule for a document of <paramref name="version"/>, before
    /// <see cref="ValidationContext.SeverityOverrides"/> are applied. Rules whose level depends on
    /// the OpenAPI version (for example, <c>response.description</c>: an error for 3.0 and 3.1, a
    /// warning for 3.2, where a response description is optional) override it; all others return
    /// <see cref="DefaultSeverity"/>.
    /// </summary>
    /// <param name="version">The version of the validated document; <see langword="null"/> when unknown.</param>
    ValidationSeverity GetDefaultSeverity(OpenApiSpecVersion? version) => DefaultSeverity;

    /// <summary>
    /// Validates <paramref name="document"/> against this rule and yields one
    /// <see cref="ValidationViolation"/> per finding.
    /// Must never throw — errors should be swallowed and treated as no violations.
    /// </summary>
    /// <param name="document">The OpenAPI document to validate.</param>
    /// <param name="context">Validation options and CLR bindings.</param>
    IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context);
}
