using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ValidationLevel = Corvus.Json.ValidationLevel;
using CorvusSchema = Corvus.Json.Validator.JsonSchema;

namespace DotNetOpenApiExtract.Core.Tests.Conformance;

/// <summary>Outcome of validating one instance against one schema.</summary>
/// <param name="IsValid">Whether the instance satisfies the schema.</param>
/// <param name="Errors">Evaluator messages for a failed validation; empty when valid.</param>
internal sealed record ConformanceResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Validates JSON instances against schemas of a serialized OpenAPI 3.1/3.2 document with a real
/// JSON Schema 2020-12 evaluator, resolving <c>#/components/schemas/*</c> references.
/// </summary>
/// <remarks>
/// <para>
/// The document's component schemas are moved under <c>$defs</c> of a generated 2020-12 schema and
/// their references rewritten accordingly; OpenAPI-only keywords (<c>discriminator</c>,
/// <c>example</c>, …) are unknown keywords there and do not affect validation.
/// </para>
/// <para>
/// <c>contentSchema</c> and <c>itemSchema</c> are annotations in 2020-12: the evaluator never applies
/// them. Validate the embedded content explicitly with <see cref="ValidateSchema"/>, passing the
/// annotation schema taken from the document and the extracted instance.
/// </para>
/// <para>
/// OpenAPI 3.0 schemas are not JSON Schema 2020-12 (<c>nullable</c>, boolean exclusive bounds), so
/// only 3.1 and 3.2 documents are accepted.
/// </para>
/// </remarks>
internal sealed class SchemaConformance
{
    private const string ComponentsPrefix = "#/components/schemas/";
    private const string DefsPrefix = "#/$defs/";

    private readonly JsonObject _defs;
    private readonly string _documentHash;

    private SchemaConformance(JsonObject defs, string documentHash)
    {
        _defs = defs;
        _documentHash = documentHash;
    }

    /// <summary>Prepares validation against the schemas of <paramref name="document"/> (serialized 3.1 or 3.2).</summary>
    public static SchemaConformance For(JsonNode document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var openapi = document["openapi"]?.GetValue<string>()
            ?? throw new ArgumentException("Not an OpenAPI document: no 'openapi' field.", nameof(document));
        if (!openapi.StartsWith("3.1", StringComparison.Ordinal) && !openapi.StartsWith("3.2", StringComparison.Ordinal))
            throw new ArgumentException(
                $"Schemas of an OpenAPI {openapi} document are not JSON Schema 2020-12; use a 3.1 or 3.2 document.",
                nameof(document));

        var schemas = document["components"]?["schemas"]?.DeepClone() as JsonObject ?? [];
        RewriteReferences(schemas);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schemas.ToJsonString())))[..16];
        return new SchemaConformance(schemas, hash);
    }

    /// <summary>Validates <paramref name="instance"/> against component <paramref name="componentId"/>.</summary>
    public ConformanceResult ValidateComponent(string componentId, JsonNode? instance)
    {
        EnsureComponent(componentId);
        return Validate(new JsonObject { ["$ref"] = DefsPrefix + EncodePointer(componentId) }, instance);
    }

    /// <summary>
    /// Validates <paramref name="instance"/> against <paramref name="schema"/>, a schema taken from the
    /// same document (for example a <c>contentSchema</c> or an <c>itemSchema</c>); its component
    /// references resolve against the document.
    /// </summary>
    public ConformanceResult ValidateSchema(JsonNode schema, JsonNode? instance)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var copy = schema.DeepClone();
        RewriteReferences(copy);
        return Validate(copy, instance);
    }

    /// <summary>
    /// Number of <c>oneOf</c> branches of component <paramref name="componentId"/> that
    /// <paramref name="instance"/> satisfies; a valid <c>oneOf</c> instance matches exactly one.
    /// </summary>
    public int CountMatchingOneOfBranches(string componentId, JsonNode? instance)
    {
        EnsureComponent(componentId);
        var oneOf = _defs[componentId]?["oneOf"] as JsonArray
            ?? throw new ArgumentException($"Component '{componentId}' has no 'oneOf'.", nameof(componentId));

        var matches = 0;
        for (var i = 0; i < oneOf.Count; i++)
        {
            var branch = new JsonObject { ["$ref"] = $"{DefsPrefix}{EncodePointer(componentId)}/oneOf/{i}" };
            if (Validate(branch, instance).IsValid)
                matches++;
        }

        return matches;
    }

    private ConformanceResult Validate(JsonNode rootSchema, JsonNode? instance)
    {
        var schemaDocument = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
        };
        foreach (var (key, value) in rootSchema.AsObject())
            schemaDocument[key] = value?.DeepClone();
        schemaDocument["$defs"] = _defs.DeepClone();

        var text = schemaDocument.ToJsonString();
        var schemaHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

        // The evaluator compiles and caches schemas by canonical URI: equal text, equal URI.
        var schema = CorvusSchema.FromText(text, $"https://openapi-extract.test/{_documentHash}/{schemaHash}.json");

        using var parsed = JsonDocument.Parse(instance?.ToJsonString() ?? "null");
        var result = schema.Validate(parsed.RootElement, ValidationLevel.Detailed);

        var errors = result.IsValid
            ? []
            : result.Results.Where(r => !r.Valid).Select(r => $"{r.Location}: {r.Message}").ToList();
        return new ConformanceResult(result.IsValid, errors);
    }

    private void EnsureComponent(string componentId)
    {
        if (!_defs.ContainsKey(componentId))
            throw new ArgumentException($"The document has no component schema '{componentId}'.", nameof(componentId));
    }

    private static void RewriteReferences(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (key == "$ref" && obj[key] is JsonValue value
                        && value.TryGetValue<string>(out var reference)
                        && reference.StartsWith(ComponentsPrefix, StringComparison.Ordinal))
                        obj[key] = DefsPrefix + reference[ComponentsPrefix.Length..];
                    else
                        RewriteReferences(obj[key]);
                }
                break;

            case JsonArray array:
                foreach (var item in array)
                    RewriteReferences(item);
                break;
        }
    }

    private static string EncodePointer(string token) =>
        token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
