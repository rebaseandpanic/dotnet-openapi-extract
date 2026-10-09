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
public sealed record ConformanceResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Validates JSON instances against schemas of a serialized OpenAPI 3.1/3.2 document with a real
/// JSON Schema 2020-12 evaluator, resolving <c>#/components/schemas/*</c> references.
/// </summary>
/// <remarks>
/// <para>
/// The document's component schemas are moved under <c>$defs</c> of a generated 2020-12 schema and
/// the references in schema positions rewritten accordingly (literal values such as <c>const</c> or
/// <c>examples</c> are data and stay untouched); OpenAPI-only keywords (<c>discriminator</c>,
/// <c>example</c>, …) are unknown keywords there and do not affect validation.
/// </para>
/// <para>
/// <c>contentSchema</c> and <c>itemSchema</c> are annotations in 2020-12: the evaluator never applies
/// them. Validate the embedded content explicitly with <see cref="ValidateSchema"/>, passing the
/// annotation schema taken from the document and the extracted instance.
/// </para>
/// <para>
/// <c>contentMediaType</c> and <c>contentEncoding</c> are annotations too (2020-12 Validation §8: an
/// implementation may assert them, a schema does not require it). The evaluator used here asserts
/// them, which would reject, for example, the empty data of a server-sent event; they are removed
/// before evaluation, and the content they describe is checked explicitly as above.
/// </para>
/// <para>
/// OpenAPI 3.0 schemas are not JSON Schema 2020-12 (<c>nullable</c>, boolean exclusive bounds), so
/// only 3.1 and 3.2 documents are accepted.
/// </para>
/// </remarks>
public sealed class SchemaConformance
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
        foreach (var (_, schema) in schemas)
            RewriteReferences(schema);

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
        // The root goes under allOf, so a boolean schema (true / false) is as valid a root as an object.
        var schemaDocument = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["allOf"]   = new JsonArray(rootSchema.DeepClone()),
            ["$defs"]   = _defs.DeepClone(),
        };

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

    /// <summary>Keywords whose value is one schema.</summary>
    private static readonly HashSet<string> SchemaKeywords = new(StringComparer.Ordinal)
    {
        "items", "additionalItems", "additionalProperties", "not", "if", "then", "else", "contains",
        "propertyNames", "unevaluatedItems", "unevaluatedProperties", "contentSchema", "itemSchema",
    };

    /// <summary>Keywords whose value is an array of schemas.</summary>
    private static readonly HashSet<string> SchemaArrayKeywords = new(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf", "prefixItems",
    };

    /// <summary>Keywords whose value is an object of schemas.</summary>
    private static readonly HashSet<string> SchemaMapKeywords = new(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "dependentSchemas", "$defs", "definitions",
    };

    /// <summary>
    /// Rewrites component references of <paramref name="schema"/> and of its subschemas, and removes
    /// the content annotations from them. Only schema
    /// positions are visited: values of <c>const</c>, <c>enum</c>, <c>default</c>, <c>example</c>,
    /// <c>examples</c> and other annotations are data and keep any <c>$ref</c> they contain as is.
    /// </summary>
    private static void RewriteReferences(JsonNode? schema)
    {
        if (schema is not JsonObject obj)
            return; // boolean schema or not a schema

        // Content annotations (see the class remarks).
        obj.Remove("contentMediaType");
        obj.Remove("contentEncoding");

        if (obj["$ref"] is JsonValue value
            && value.TryGetValue<string>(out var reference)
            && reference.StartsWith(ComponentsPrefix, StringComparison.Ordinal))
            obj["$ref"] = DefsPrefix + reference[ComponentsPrefix.Length..];

        foreach (var (key, child) in obj.ToList())
        {
            if (SchemaKeywords.Contains(key))
            {
                RewriteReferences(child);
            }
            else if (SchemaArrayKeywords.Contains(key) && child is JsonArray array)
            {
                foreach (var item in array)
                    RewriteReferences(item);
            }
            else if (SchemaMapKeywords.Contains(key) && child is JsonObject map)
            {
                foreach (var (_, item) in map)
                    RewriteReferences(item);
            }
        }
    }

    private static string EncodePointer(string token) =>
        token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
