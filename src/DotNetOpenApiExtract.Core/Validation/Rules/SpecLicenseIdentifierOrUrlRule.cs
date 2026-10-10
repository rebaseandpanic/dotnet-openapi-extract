using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.license-identifier-or-url</c>
/// OpenAPI 3.1 and 3.2: <c>info.license.identifier</c> and <c>info.license.url</c> are mutually
/// exclusive. OpenAPI 3.0 has no <c>identifier</c>, so the rule does not apply there. An unknown
/// version is checked (the field itself only exists since 3.1).
/// </summary>
public sealed class SpecLicenseIdentifierOrUrlRule : IValidationRule
{
    public string Id => "spec.license-identifier-or-url";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (context.OpenApiSpecVersion == OpenApiSpecVersion.OpenApi3_0)
            yield break;

        var license = document.Info?.License;
        if (license == null || string.IsNullOrEmpty(license.Identifier) || license.Url == null)
            yield break;

        yield return new ValidationViolation(
            Id,
            DefaultSeverity,
            "#/info/license",
            null,
            $"The license has both an identifier ('{license.Identifier}') and a url ('{license.Url.OriginalString}'); " +
            "OpenAPI allows only one of them.");
    }
}
