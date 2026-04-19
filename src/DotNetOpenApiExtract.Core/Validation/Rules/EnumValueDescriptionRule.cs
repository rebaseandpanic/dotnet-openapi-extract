using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>enum.value-description</c>
/// Enum schemas must carry the <c>x-enum-descriptions</c> extension (added by Task 6.1),
/// and every entry in the array must be a non-empty string.
/// <para>
/// Contract (Task 6.1): <c>x-enum-descriptions</c> is a <c>JsonNodeExtension</c> wrapping a
/// <c>JsonArray</c> of strings. Length matches <c>enum[]</c>. Empty strings mean "no doc".
/// </para>
/// </summary>
public sealed class EnumValueDescriptionRule : IValidationRule
{
    public string Id => "enum.value-description";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    private const string ExtensionKey = "x-enum-descriptions";

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s) continue;
            if (s.Enum == null || s.Enum.Count == 0) continue;

            // Missing extension entirely
            if (s.Extensions == null || !s.Extensions.TryGetValue(ExtensionKey, out var extensionObj))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    resolver.ForSchema(schemaId),
                    $"Enum schema '{schemaId}' is missing the '{ExtensionKey}' extension.");
                continue;
            }

            // Try to read as array of strings
            if (!TryReadEnumDescriptions(extensionObj, out var descriptions))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    resolver.ForSchema(schemaId),
                    $"Enum schema '{schemaId}' has malformed '{ExtensionKey}' extension (expected string array matching enum length).");
                continue;
            }

            // Check length matches
            if (descriptions.Length != s.Enum.Count)
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    resolver.ForSchema(schemaId),
                    $"Enum schema '{schemaId}' has '{ExtensionKey}' with {descriptions.Length} entries but {s.Enum.Count} enum values.");
                continue;
            }

            // Check each entry is non-empty
            for (int i = 0; i < descriptions.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(descriptions[i]))
                {
                    var enumValueStr = GetStringValue(s.Enum[i]) ?? $"[{i}]";
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForSchema(schemaId),
                        resolver.ForSchema(schemaId),
                        $"Enum schema '{schemaId}' value '{enumValueStr}' (index {i}) has no description in '{ExtensionKey}'.");
                }
            }
        }
    }

    private static string? GetStringValue(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node == null) return null;
        if (node is System.Text.Json.Nodes.JsonValue jv)
        {
            if (jv.TryGetValue<string>(out var s)) return s;
            return jv.ToString();
        }
        return node.ToString();
    }

    /// <summary>
    /// Tries to read <c>x-enum-descriptions</c> extension as a string array.
    /// Returns false if the extension is present but malformed.
    /// </summary>
    internal static bool TryReadEnumDescriptions(IOpenApiExtension extension, out string[] descriptions)
    {
        descriptions = [];

        // The extension is expected to be a JsonNodeExtension wrapping a JsonArray
        // We use duck typing via the extension's string representation or JsonNode property
        if (extension is not Microsoft.OpenApi.JsonNodeExtension nodeExt)
            return false;

        if (nodeExt.Node is not JsonArray arr)
            return false;

        var result = new string[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            var item = arr[i];
            result[i] = item?.GetValue<string>() ?? string.Empty;
        }

        descriptions = result;
        return true;
    }
}
