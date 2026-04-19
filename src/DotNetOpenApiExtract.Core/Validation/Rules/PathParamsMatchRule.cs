using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>path.params-match</c>
/// Bidirectional check between path template variables and declared <c>in: path</c> parameters.
/// <list type="bullet">
/// <item>Every <c>{variable}</c> in the path template must be backed by a parameter with
/// <c>in: path</c> declared on the operation or the path item.</item>
/// <item>Every <c>in: path</c> parameter must correspond to a <c>{variable}</c> in the template.</item>
/// </list>
/// OAS 3.0.3 MUST-level constraint. Spectral <c>path-params</c> severity 0.
/// </summary>
public sealed class PathParamsMatchRule : IValidationRule
{
    private static readonly Regex TemplateVarRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    public string Id => "path.params-match";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item) continue;

            // Extract template variable names from the path string
            var templateVars = new HashSet<string>(
                TemplateVarRegex.Matches(path).Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);

            // Path-level parameters declared on the path item itself
            var pathLevelParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (item.Parameters != null)
            {
                foreach (var p in item.Parameters)
                    if (p?.In == ParameterLocation.Path && !string.IsNullOrWhiteSpace(p.Name))
                        pathLevelParams.Add(p.Name);
            }

            if (item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var opPtr = JsonPointerHelper.ForOperation(path, method.ToString());

                // Collect in:path parameter names for this operation (operation-level overrides path-level)
                var opPathParams = new HashSet<string>(pathLevelParams, StringComparer.OrdinalIgnoreCase);
                if (operation.Parameters != null)
                {
                    foreach (var p in operation.Parameters)
                        if (p?.In == ParameterLocation.Path && !string.IsNullOrWhiteSpace(p.Name))
                            opPathParams.Add(p.Name);
                }

                // Template var must have a parameter
                foreach (var varName in templateVars.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
                {
                    if (!opPathParams.Contains(varName))
                    {
                        yield return new ValidationViolation(
                            Id, DefaultSeverity,
                            $"{opPtr}/parameters",
                            null,
                            $"Path template variable '{{{varName}}}' in '{path}' has no corresponding in:path parameter on {method.ToString().ToUpperInvariant()} operation.");
                    }
                }

                // In:path parameter must have a template var
                foreach (var paramName in opPathParams.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (!templateVars.Contains(paramName))
                    {
                        yield return new ValidationViolation(
                            Id, DefaultSeverity,
                            $"{opPtr}/parameters",
                            null,
                            $"in:path parameter '{paramName}' on {method.ToString().ToUpperInvariant()} '{path}' has no corresponding template variable in the path.");
                    }
                }
            }
        }
    }
}
