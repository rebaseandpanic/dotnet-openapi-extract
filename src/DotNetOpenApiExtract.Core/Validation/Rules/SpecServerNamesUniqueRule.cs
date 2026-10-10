using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.server-names-unique</c>
/// OpenAPI 3.2: the names of the top-level <c>servers</c> are unique (compared exactly; servers
/// without a name are not compared). Earlier versions have no <c>name</c> (the tool writes it as an
/// extension there), so the rule applies to 3.2 and to an unknown version. One violation per
/// repeated server, at its name.
/// </summary>
public sealed class SpecServerNamesUniqueRule : IValidationRule
{
    public string Id => "spec.server-names-unique";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (context.OpenApiSpecVersion is not (null or OpenApiSpecVersion.OpenApi3_2) || document.Servers == null)
            yield break;

        var firstIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < document.Servers.Count; index++)
        {
            var name = document.Servers[index]?.Name;
            if (name == null)
                continue;

            if (firstIndex.TryAdd(name, index))
                continue;

            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                $"#/servers/{index}/name",
                null,
                $"Server name '{name}' is already used by servers[{firstIndex[name]}]; server names must be unique.");
        }
    }
}
