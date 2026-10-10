using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>tag.parent-defined</c>
/// OpenAPI 3.2: the <c>parent</c> of a tag must name a tag declared in the top-level <c>tags</c>
/// (names compared exactly). Earlier versions have no <c>parent</c> (the tool writes it as an
/// extension there), so the rule applies to 3.2 and to an unknown version.
/// </summary>
public sealed class TagParentDefinedRule : IValidationRule
{
    public string Id => "tag.parent-defined";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (!TagHierarchy.Applies(context) || document.Tags == null)
            yield break;

        var tags = document.Tags.ToList();
        var names = new HashSet<string>(tags.Where(t => t?.Name != null).Select(t => t.Name!), StringComparer.Ordinal);

        for (var index = 0; index < tags.Count; index++)
        {
            var parent = TagHierarchy.ParentName(tags[index]);
            if (parent == null || names.Contains(parent))
                continue;

            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                $"#/tags/{index}/parent",
                null,
                $"Tag '{tags[index].Name}' has the parent '{parent}', which is not declared in the top-level tags.");
        }
    }
}
