using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>tag.no-parent-cycle</c>
/// OpenAPI 3.2: following <c>parent</c> from tag to tag must never come back to a tag already
/// visited — the hierarchy is a tree. One violation per cycle (a tag that is its own parent is a
/// cycle too), at the first tag of the cycle in document order; tags that only lead into a cycle are
/// not reported separately, and a parent that is not declared ends the chain (see
/// <c>tag.parent-defined</c>). Applies to 3.2 and to an unknown version.
/// </summary>
public sealed class TagNoParentCycleRule : IValidationRule
{
    public string Id => "tag.no-parent-cycle";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (!TagHierarchy.Applies(context) || document.Tags == null)
            yield break;

        var tags = document.Tags.ToList();

        // The first declaration of a name decides its parent; duplicates are tag.no-duplicates.
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < tags.Count; index++)
        {
            if (tags[index]?.Name is { } name)
                indexByName.TryAdd(name, index);
        }

        var reported = new HashSet<int>();
        for (var start = 0; start < tags.Count; start++)
        {
            if (reported.Contains(start) || tags[start]?.Name == null)
                continue;

            // Walk the chain; a revisit closes a cycle, which consists of the tags from the first
            // occurrence of the revisited tag onwards.
            var path = new List<int>();
            var position = new Dictionary<int, int>();
            int? current = start;
            while (current is { } at && !position.ContainsKey(at))
            {
                position[at] = path.Count;
                path.Add(at);
                var parent = TagHierarchy.ParentName(tags[at]);
                current = parent != null && indexByName.TryGetValue(parent, out var next) ? next : null;
            }

            if (current is not { } revisited)
                continue;

            var cycle = path.Skip(position[revisited]).ToList();
            if (!cycle.Contains(start) || cycle.Any(reported.Contains))
                continue;

            foreach (var member in cycle)
                reported.Add(member);

            var first = cycle.Min();
            var rotated = cycle.SkipWhile(i => i != first).Concat(cycle.TakeWhile(i => i != first)).ToList();
            var chain = string.Join(" → ", rotated.Append(first).Select(i => $"'{tags[i].Name}'"));
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                $"#/tags/{first}/parent",
                null,
                $"The parent chain of tag '{tags[first].Name}' is a cycle: {chain}.");
        }
    }
}

/// <summary>What the two tag hierarchy rules share.</summary>
internal static class TagHierarchy
{
    /// <summary><c>parent</c> exists only in OpenAPI 3.2; an unknown version is checked.</summary>
    public static bool Applies(ValidationContext context) =>
        context.OpenApiSpecVersion is null or OpenApiSpecVersion.OpenApi3_2;

    /// <summary>The name a tag's <c>parent</c> refers to, or <see langword="null"/> without a parent.</summary>
    public static string? ParentName(OpenApiTag? tag) =>
        tag?.Parent == null ? null : tag.Parent.Reference?.Id ?? tag.Parent.Name;
}
