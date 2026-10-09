using System.Text.Json.Nodes;

namespace DotNetOpenApiExtract.Core.Tests.Harness;

/// <summary>Finds local <c>$ref</c> values in a serialized document and resolves JSON pointers.</summary>
internal static class JsonReferences
{
    /// <summary>Every <c>$ref</c> string value in <paramref name="node"/>, depth-first.</summary>
    public static IEnumerable<string> All(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key == "$ref" && value is JsonValue refValue && refValue.TryGetValue<string>(out var reference))
                        yield return reference;
                    else
                        foreach (var nested in All(value))
                            yield return nested;
                }
                break;

            case JsonArray array:
                foreach (var item in array)
                    foreach (var nested in All(item))
                        yield return nested;
                break;
        }
    }

    /// <summary>Resolves a local reference (<c>#/a/b</c>) against <paramref name="root"/>; null when missing.</summary>
    public static JsonNode? Resolve(JsonNode root, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            return null;

        JsonNode? current = root;
        foreach (var raw in reference[2..].Split('/'))
        {
            var token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            current = current is JsonObject obj && obj.TryGetPropertyValue(token, out var next) ? next : null;
            if (current == null)
                return null;
        }

        return current;
    }
}
