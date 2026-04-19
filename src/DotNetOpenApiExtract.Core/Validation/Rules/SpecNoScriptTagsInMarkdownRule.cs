using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.no-script-tags-in-markdown</c>
/// Description fields must not contain <c>&lt;script&gt;</c> (case-insensitive). API portals that
/// render markdown descriptions may execute injected JavaScript, creating an XSS vector.
/// <para>
/// This rule is <b>off by default</b>. Enable with <c>--enable-rule spec.no-script-tags-in-markdown</c>.
/// </para>
/// Spectral <c>no-script-tags-in-markdown</c> recommended.
/// </summary>
public sealed class SpecNoScriptTagsInMarkdownRule : IValidationRule
{
    private const string ScriptTag = "<script";

    public string Id => "spec.no-script-tags-in-markdown";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        // info.description
        if (ContainsScript(document.Info?.Description))
        {
            yield return new ValidationViolation(Id, DefaultSeverity, "#/info/description", null,
                "info.description contains '<script>' which may be a security risk in rendered API portals.");
        }

        // Top-level tags
        if (document.Tags != null)
        {
            foreach (var tag in document.Tags.OrderBy(t => t?.Name ?? ""))
            {
                if (ContainsScript(tag?.Description))
                    yield return new ValidationViolation(Id, DefaultSeverity, "#/tags", null,
                        $"Tag '{tag!.Name}' description contains '<script>' which may be a security risk.");
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

                    if (ContainsScript(operation.Description))
                        yield return new ValidationViolation(Id, DefaultSeverity, opPtr, null,
                            $"Operation {method.ToString().ToUpperInvariant()} '{path}' description contains '<script>'.");

                    if (ContainsScript(operation.Summary))
                        yield return new ValidationViolation(Id, DefaultSeverity, opPtr, null,
                            $"Operation {method.ToString().ToUpperInvariant()} '{path}' summary contains '<script>'.");
                }
            }
        }

        // Components / schemas
        if (document.Components?.Schemas != null)
        {
            foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
            {
                if (schema is OpenApiSchema s && ContainsScript(s.Description))
                    yield return new ValidationViolation(Id, DefaultSeverity,
                        JsonPointerHelper.ForSchema(schemaId), null,
                        $"Schema '{schemaId}' description contains '<script>'.");
            }
        }
    }

    private static bool ContainsScript(string? text)
        => text != null && text.Contains(ScriptTag, StringComparison.OrdinalIgnoreCase);
}
