using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.no-eval-in-markdown</c>
/// Description fields must not contain <c>eval(</c>. API portals that render markdown
/// descriptions may execute injected JavaScript, creating an XSS vector.
/// <para>
/// This rule is <b>off by default</b>. Enable with <c>--enable-rule spec.no-eval-in-markdown</c>.
/// </para>
/// Spectral <c>no-eval-in-markdown</c> recommended.
/// </summary>
public sealed class SpecNoEvalInMarkdownRule : IValidationRule
{
    private const string EvalPattern = "eval(";

    public string Id => "spec.no-eval-in-markdown";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        // info.description
        if (ContainsEval(document.Info?.Description))
        {
            yield return new ValidationViolation(Id, DefaultSeverity, "#/info/description", null,
                "info.description contains 'eval(' which may be a security risk in rendered API portals.");
        }

        // Top-level tags
        if (document.Tags != null)
        {
            foreach (var tag in document.Tags.OrderBy(t => t?.Name ?? ""))
            {
                if (ContainsEval(tag?.Description))
                    yield return new ValidationViolation(Id, DefaultSeverity, "#/tags", null,
                        $"Tag '{tag!.Name}' description contains 'eval(' which may be a security risk.");
            }
        }

        // Paths / operations
        if (document.Paths != null)
        {
            foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
            {
                if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

                foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
                {
                    var opPtr = JsonPointerHelper.ForOperation(path, method.ToString());

                    if (ContainsEval(operation.Description))
                        yield return new ValidationViolation(Id, DefaultSeverity, opPtr, null,
                            $"Operation {method.ToString().ToUpperInvariant()} '{path}' description contains 'eval('.");

                    if (ContainsEval(operation.Summary))
                        yield return new ValidationViolation(Id, DefaultSeverity, opPtr, null,
                            $"Operation {method.ToString().ToUpperInvariant()} '{path}' summary contains 'eval('.");
                }
            }
        }

        // Components / schemas
        if (document.Components?.Schemas != null)
        {
            foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
            {
                if (schema is OpenApiSchema s && ContainsEval(s.Description))
                    yield return new ValidationViolation(Id, DefaultSeverity,
                        JsonPointerHelper.ForSchema(schemaId), null,
                        $"Schema '{schemaId}' description contains 'eval('.");
            }
        }
    }

    private static bool ContainsEval(string? text)
        => text != null && text.Contains(EvalPattern, StringComparison.OrdinalIgnoreCase);
}
