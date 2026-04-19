using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.servers-defined</c>
/// The <c>servers</c> array should be present and non-empty.
/// Missing servers causes API explorer tools (SwaggerUI, Redoc) to use relative URLs,
/// which often break in hosted environments.
/// <para>
/// This rule is <b>off by default</b> — many internal specs legitimately omit the servers array
/// when it is injected at the gateway level. Enable with <c>--enable-rule spec.servers-defined</c>.
/// </para>
/// Spectral <c>oas3-api-servers</c> recommended. Redocly <c>no-empty-servers</c> error.
/// </summary>
public sealed class SpecServersDefinedRule : IValidationRule
{
    public string Id => "spec.servers-defined";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Servers == null || document.Servers.Count == 0)
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                "#/servers",
                null,
                "The servers array is missing or empty. API explorer tools may not work correctly without a base URL.");
        }
    }
}
