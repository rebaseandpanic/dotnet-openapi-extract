using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>security.scheme-defined</c>
/// If any operation references a security scheme, that scheme must be defined in
/// <c>components/securitySchemes</c>.
/// </summary>
public sealed class SecuritySchemeDefinedRule : IValidationRule
{
    public string Id => "security.scheme-defined";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        var definedSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.Components?.SecuritySchemes != null)
        {
            foreach (var schemeName in document.Components.SecuritySchemes.Keys)
                definedSchemes.Add(schemeName);
        }

        // Also check global security
        var referencedSchemes = new List<(string schemeName, string pointer)>();

        if (document.Security != null)
        {
            foreach (var requirement in document.Security)
            {
                foreach (var (scheme, _) in requirement)
                {
                    var name = GetSchemeName(scheme);
                    if (name != null)
                        referencedSchemes.Add((name, "#/security"));
                }
            }
        }

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                if (operation.Security == null) continue;

                foreach (var requirement in operation.Security)
                {
                    foreach (var (scheme, _) in requirement)
                    {
                        var name = GetSchemeName(scheme);
                        if (name != null)
                            referencedSchemes.Add((name, JsonPointerHelper.ForOperation(path, method.ToString())));
                    }
                }
            }
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (schemeName, pointer) in referencedSchemes.OrderBy(r => r.schemeName))
        {
            if (!definedSchemes.Contains(schemeName) && reported.Add(schemeName))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    pointer,
                    null,
                    $"Security scheme '{schemeName}' is referenced but not defined in components/securitySchemes.");
            }
        }
    }

    private static string? GetSchemeName(IOpenApiSecurityScheme scheme)
    {
        if (scheme is OpenApiSecuritySchemeReference refScheme)
            return refScheme.Reference?.Id;
        if (scheme is OpenApiSecurityScheme concreteScheme)
            return concreteScheme.Name;
        return null;
    }
}
