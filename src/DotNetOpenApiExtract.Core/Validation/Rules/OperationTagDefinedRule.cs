using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.tag-defined</c>
/// Each tag referenced by an operation must exist in the top-level <c>tags</c> array.
/// Undefined tags produce broken navigation in Redoc and SwaggerUI.
/// Spectral <c>operation-tag-defined</c> recommended. Redocly <c>operation-tag-defined</c> warn.
/// </summary>
public sealed class OperationTagDefinedRule : IValidationRule
{
    public string Id => "operation.tag-defined";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Paths == null) yield break;

        var definedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.Tags != null)
        {
            foreach (var tag in document.Tags)
                if (tag?.Name != null)
                    definedTags.Add(tag.Name);
        }

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                if (operation.Tags == null) continue;
                var opPtr = JsonPointerHelper.ForOperation(path, method.ToString());

                foreach (var tag in operation.Tags.OrderBy(t => t?.Name ?? ""))
                {
                    if (tag?.Name == null) continue;
                    if (!definedTags.Contains(tag.Name))
                    {
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            opPtr,
                            null,
                            $"Tag '{tag.Name}' used on operation {method.ToString().ToUpperInvariant()} '{path}' is not defined in the top-level tags array.");
                    }
                }
            }
        }
    }
}
