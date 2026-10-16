using System.Text.Json.Nodes;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>
/// An object of a source document with <c>$ref</c> and other keys, as found by
/// <see cref="RefSiblingScanner.Scan"/>.
/// </summary>
/// <param name="JsonPointer">RFC 6901 pointer of the object (<c>#/components/schemas/A/properties/b</c>).</param>
/// <param name="Siblings">The keys next to <c>$ref</c>, in document order.</param>
public sealed record RefSiblingOccurrence(string JsonPointer, IReadOnlyList<string> Siblings);

/// <summary>
/// Finds the Reference Objects of an OpenAPI 3.0 document, as read from its file, that carry keys next
/// to <c>$ref</c>.
/// </summary>
/// <remarks>
/// <para>
/// OpenAPI 3.0 (Reference Object): "This object cannot be extended with additional properties and any
/// properties added SHALL be ignored." A reader that builds the object model drops such keys without a
/// diagnostic (Microsoft.OpenApi 3.10.2 does), so they can only be seen in the source tree, before the
/// model is built.
/// </para>
/// <para>
/// Every object with a string <c>$ref</c> and another key is reported, except where <c>$ref</c> is not a
/// reference: a Path Item Object (its <c>$ref</c> field stands next to its other fields), the key of a
/// map of names (a property named <c>$ref</c> in <c>properties</c>, an entry of <c>responses</c>,
/// <c>content</c>, the maps of <c>components</c> and the like), and data — the values of
/// <c>example</c>, <c>default</c>, <c>enum</c>, specification extensions (<c>x-…</c>), the
/// <c>value</c> of an Example Object and the <c>parameters</c> and <c>requestBody</c> of a Link Object.
/// The keys of a reported object are not searched further: a 3.0 reader ignores all of them.
/// </para>
/// </remarks>
public static class RefSiblingScanner
{
    private const string RefKey = "$ref";

    /// <summary>What an object of the document is, as far as the search needs to know.</summary>
    private enum Kind
    {
        /// <summary>An object whose keys are fields: a reference when it has <c>$ref</c>.</summary>
        Object,

        /// <summary>A Path Item Object: its <c>$ref</c> is a field of its own.</summary>
        PathItem,

        /// <summary>An Example Object: its <c>value</c> is data.</summary>
        Example,

        /// <summary>A Link Object: its <c>parameters</c> and <c>requestBody</c> are data.</summary>
        Link,

        /// <summary>A Callback Object (or a reference to one): a map of expressions to Path Items.</summary>
        Callback,
    }

    /// <summary>Field values that are data, not objects of the document.</summary>
    private static readonly HashSet<string> DataFields = new(StringComparer.Ordinal) { "example", "default", "enum" };

    /// <summary>
    /// Fields whose value, when it is an object, is a map from names to objects of one kind; the
    /// names are keys of the map, never fields.
    /// </summary>
    private static readonly Dictionary<string, Kind> MapFields = new(StringComparer.Ordinal)
    {
        ["paths"]           = Kind.PathItem,
        ["callbacks"]       = Kind.Callback,
        ["examples"]        = Kind.Example,
        ["links"]           = Kind.Link,
        ["schemas"]         = Kind.Object,
        ["responses"]       = Kind.Object,
        ["parameters"]      = Kind.Object,
        ["requestBodies"]   = Kind.Object,
        ["headers"]         = Kind.Object,
        ["securitySchemes"] = Kind.Object,
        ["properties"]      = Kind.Object,
        ["content"]         = Kind.Object,
        ["encoding"]        = Kind.Object,
        ["variables"]       = Kind.Object,
        ["mapping"]         = Kind.Object,
        ["scopes"]          = Kind.Object,
    };

    /// <summary>
    /// The objects of <paramref name="document"/> (the root of a 3.0 document as read from JSON or
    /// YAML) that have keys next to <c>$ref</c>, in document order.
    /// </summary>
    public static IReadOnlyList<RefSiblingOccurrence> Scan(JsonNode? document)
    {
        var found = new List<RefSiblingOccurrence>();
        if (document is JsonObject root)
            Walk(root, Kind.Object, "#", found);
        return found;
    }

    private static void Walk(JsonObject obj, Kind kind, string pointer, List<RefSiblingOccurrence> found)
    {
        if (kind != Kind.PathItem && obj[RefKey] is JsonValue reference && reference.TryGetValue<string>(out _))
        {
            if (obj.Count > 1)
                found.Add(new RefSiblingOccurrence(pointer, obj.Select(p => p.Key).Where(k => k != RefKey).ToList()));
            return;
        }

        if (kind == Kind.Callback)
        {
            WalkMap(obj, Kind.PathItem, pointer, found);
            return;
        }

        foreach (var (key, value) in obj)
        {
            if (key.StartsWith("x-", StringComparison.Ordinal) || DataFields.Contains(key)
                || (kind == Kind.Example && key == "value")
                || (kind == Kind.Link && key is "parameters" or "requestBody"))
                continue;

            var child = $"{pointer}/{JsonPointerHelper.EncodeSegment(key)}";
            if (key == "security" && value is JsonArray requirements)
            {
                // Security Requirement Objects: maps of scheme names to scope lists.
                for (var i = 0; i < requirements.Count; i++)
                    if (requirements[i] is JsonObject requirement)
                        WalkMap(requirement, Kind.Object, $"{child}/{i}", found);
            }
            else if (value is JsonObject map && MapFields.TryGetValue(key, out var entryKind))
            {
                WalkMap(map, entryKind, child, found);
            }
            else
            {
                WalkValue(value, child, found);
            }
        }
    }

    private static void WalkMap(JsonObject map, Kind entryKind, string pointer, List<RefSiblingOccurrence> found)
    {
        foreach (var (name, entry) in map)
        {
            if (entry is JsonObject entryObject)
                Walk(entryObject, entryKind, $"{pointer}/{JsonPointerHelper.EncodeSegment(name)}", found);
        }
    }

    private static void WalkValue(JsonNode? value, string pointer, List<RefSiblingOccurrence> found)
    {
        switch (value)
        {
            case JsonObject obj:
                Walk(obj, Kind.Object, pointer, found);
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    WalkValue(array[i], $"{pointer}/{i}", found);
                break;
        }
    }
}
