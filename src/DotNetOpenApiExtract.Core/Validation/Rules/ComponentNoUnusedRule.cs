using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>component.no-unused</c>
/// Schemas in <c>components/schemas</c> that are never referenced anywhere in the document
/// bloat generated SDKs and are likely dead code.
/// <para>
/// This rule is <b>off by default</b>. Enable with <c>--enable-rule component.no-unused</c>.
/// </para>
/// <para>
/// Implementation note: this rule implements an approximation. It collects <c>$ref</c> IDs
/// by walking operation request/response bodies, parameter schemas, path item parameters,
/// and inline schema compositions (allOf/anyOf/oneOf/properties). It does NOT walk deep
/// into all possible reference chains (e.g., referenced schemas that reference other schemas),
/// so it may emit false positives for schemas that are transitively reachable.
/// </para>
/// Spectral <c>oas3-unused-component</c> recommended. Redocly <c>no-unused-components</c> warn.
/// </summary>
public sealed class ComponentNoUnusedRule : IValidationRule
{
    public string Id => "component.no-unused";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null || document.Components.Schemas.Count == 0)
            yield break;

        var referencedIds = new HashSet<string>(StringComparer.Ordinal);

        // Walk paths/operations to collect all referenced schema IDs
        if (document.Paths != null)
        {
            foreach (var (_, pathItem) in document.Paths)
            {
                if (pathItem is not OpenApiPathItem item) continue;

                // Path-level parameters
                if (item.Parameters != null)
                    foreach (var p in item.Parameters)
                        CollectFromSchema(p?.Schema, referencedIds);

                if (item.Operations == null) continue;

                foreach (var (_, operation) in item.Operations)
                {
                    // Operation parameters
                    if (operation.Parameters != null)
                        foreach (var p in operation.Parameters)
                            CollectFromSchema(p?.Schema, referencedIds);

                    // Request body
                    if (operation.RequestBody?.Content != null)
                        foreach (var (_, mediaType) in operation.RequestBody.Content)
                            CollectFromSchema(mediaType?.Schema, referencedIds);

                    // Responses
                    if (operation.Responses != null)
                    {
                        foreach (var (_, response) in operation.Responses)
                        {
                            if (response?.Content == null) continue;
                            foreach (var (_, mediaType) in response.Content)
                                CollectFromSchema(mediaType?.Schema, referencedIds);
                        }
                    }
                }
            }
        }

        // Walk components schemas themselves — collect self-references (for composed schemas)
        foreach (var (_, schema) in document.Components.Schemas)
            if (schema is OpenApiSchema s)
                CollectCompositionRefs(s, referencedIds);

        // Any schema in components that is not referenced
        foreach (var schemaId in document.Components.Schemas.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!referencedIds.Contains(schemaId))
            {
                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchema(schemaId),
                    null,
                    $"Schema '{schemaId}' in components/schemas is not referenced anywhere in the document (approximate check).");
            }
        }
    }

    private static void CollectFromSchema(IOpenApiSchema? schema, HashSet<string> ids)
    {
        if (schema == null) return;

        if (schema is OpenApiSchemaReference refSchema)
        {
            var id = refSchema.Reference?.Id;
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
            return;
        }

        if (schema is OpenApiSchema s)
            CollectCompositionRefs(s, ids);
    }

    private static void CollectCompositionRefs(OpenApiSchema schema, HashSet<string> ids)
    {
        // Properties
        if (schema.Properties != null)
            foreach (var (_, ps) in schema.Properties)
                CollectFromSchema(ps, ids);

        // Items
        CollectFromSchema(schema.Items, ids);

        // anyOf / allOf / oneOf
        if (schema.AnyOf != null) foreach (var s in schema.AnyOf) CollectFromSchema(s, ids);
        if (schema.AllOf != null) foreach (var s in schema.AllOf) CollectFromSchema(s, ids);
        if (schema.OneOf != null) foreach (var s in schema.OneOf) CollectFromSchema(s, ids);

        // AdditionalProperties
        if (schema.AdditionalProperties is IOpenApiSchema addProp)
            CollectFromSchema(addProp, ids);
    }
}
