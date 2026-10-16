using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>The CLR property of a component that a schema property is written from.</summary>
internal static class ClrProperties
{
    /// <summary>
    /// The public instance property of <paramref name="type"/> written as <paramref name="jsonName"/>:
    /// the one whose <c>[JsonPropertyName]</c> is that name, else the one whose name matches it under any
    /// naming policy (case, <c>_</c> and <c>-</c> ignored). A property excluded by <c>[JsonIgnore]</c> is
    /// never written and never matches. Attributes are read as the generator reads them, positional
    /// record parameters included.
    /// </summary>
    public static PropertyInfo? Find(Type type, string jsonName)
    {
        var candidates = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Attributes: AttributeHelper.GetMergedPropertyAttributes(p)))
            .Where(c => !Schema.SchemaGenerator.ShouldIgnoreProperty(c.Attributes))
            .ToList();

        foreach (var (property, attributes) in candidates)
        {
            if (JsonName(attributes) == jsonName)
                return property;
        }

        var key = Normalized(jsonName);
        return candidates.FirstOrDefault(c => JsonName(c.Attributes) == null && Normalized(c.Property.Name) == key).Property;
    }

    private static string? JsonName(IList<CustomAttributeData> attributes) =>
        AttributeHelper.GetAttribute(attributes, AttributeHelper.Names.JsonPropertyName) is { } attr
            ? AttributeHelper.GetConstructorArgument<string>(attr, 0)
            : null;

    private static string Normalized(string name) =>
        new string(name.Where(c => c != '_' && c != '-').Select(char.ToLowerInvariant).ToArray());
}
