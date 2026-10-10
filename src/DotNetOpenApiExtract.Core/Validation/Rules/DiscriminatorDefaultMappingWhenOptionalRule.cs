using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>discriminator.default-mapping-when-optional</c>
/// OpenAPI 3.2: a <c>discriminator</c> whose property is not required must have a
/// <c>defaultMapping</c>, which names the schema of an instance without the property. Earlier
/// versions have no <c>defaultMapping</c>, so the rule applies to 3.2 only.
/// <para>
/// The property is required for the schema holding the discriminator when an instance valid against
/// that schema always has it: it is listed in the schema's own <c>required</c>; or some
/// <c>allOf</c> element requires it; or every <c>oneOf</c> alternative requires it; or every
/// <c>anyOf</c> alternative requires it — each element evaluated the same way, through <c>$ref</c>
/// to its target. A <c>oneOf</c> / <c>anyOf</c> where only some alternatives require it does not make
/// it required.
/// </para>
/// <para>
/// Every discriminator of the document is checked: in component schemas and in the inline schemas
/// of operations (parameters, request bodies, responses, item schemas), with the schemas nested in
/// them. One violation per discriminator, at the <c>discriminator</c> object.
/// </para>
/// </summary>
public sealed class DiscriminatorDefaultMappingWhenOptionalRule : IValidationRule
{
    public string Id => "discriminator.default-mapping-when-optional";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (context.OpenApiSpecVersion != OpenApiSpecVersion.OpenApi3_2)
            yield break;

        var violations = new List<ValidationViolation>();
        var visited = new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance);

        if (document.Components?.Schemas != null)
        {
            foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                Walk(document, schema, JsonPointerHelper.ForSchema(schemaId), visited, violations);
        }

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;

            if (operation.Parameters != null)
            {
                for (var i = 0; i < operation.Parameters.Count; i++)
                    Walk(document, operation.Parameters[i]?.Schema, $"{op.Pointer}/parameters/{i}/schema", visited, violations);
            }

            if (operation.RequestBody?.Content != null)
                WalkContent(document, operation.RequestBody.Content, $"{op.Pointer}/requestBody/content", visited, violations);

            if (operation.Responses != null)
            {
                foreach (var (status, response) in operation.Responses.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (response?.Content != null)
                        WalkContent(document, response.Content, $"{JsonPointerHelper.ForResponseOf(op.Pointer, status)}/content", visited, violations);
                }
            }
        }

        foreach (var violation in violations)
            yield return violation;
    }

    private void WalkContent(
        OpenApiDocument document,
        IDictionary<string, IOpenApiMediaType> content,
        string pointer,
        HashSet<OpenApiSchema> visited,
        List<ValidationViolation> violations)
    {
        foreach (var (name, mediaType) in content.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var mediaPointer = $"{pointer}/{JsonPointerHelper.EncodeSegment(name)}";
            Walk(document, mediaType?.Schema, $"{mediaPointer}/schema", visited, violations);
            Walk(document, mediaType?.ItemSchema, $"{mediaPointer}/itemSchema", visited, violations);
        }
    }

    /// <summary>
    /// Checks the discriminator of <paramref name="schema"/> and the schemas nested in it. References
    /// are not followed: their targets are component schemas, walked on their own.
    /// </summary>
    private void Walk(
        OpenApiDocument document,
        IOpenApiSchema? schema,
        string pointer,
        HashSet<OpenApiSchema> visited,
        List<ValidationViolation> violations)
    {
        if (schema is not OpenApiSchema s || !visited.Add(s))
            return;

        if (s.Discriminator is { PropertyName: { Length: > 0 } property, DefaultMapping: null }
            && !Requires(document, s, property, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance)))
        {
            violations.Add(new ValidationViolation(
                Id,
                DefaultSeverity,
                $"{pointer}/discriminator",
                null,
                $"The discriminator property '{property}' is not required, so an instance may lack it; " +
                "OpenAPI 3.2 then requires a defaultMapping."));
        }

        if (s.Properties != null)
        {
            foreach (var (name, propertySchema) in s.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                Walk(document, propertySchema, $"{pointer}/properties/{JsonPointerHelper.EncodeSegment(name)}", visited, violations);
        }

        Walk(document, s.Items, $"{pointer}/items", visited, violations);
        Walk(document, s.AdditionalProperties, $"{pointer}/additionalProperties", visited, violations);
        WalkList(document, s.AllOf, $"{pointer}/allOf", visited, violations);
        WalkList(document, s.OneOf, $"{pointer}/oneOf", visited, violations);
        WalkList(document, s.AnyOf, $"{pointer}/anyOf", visited, violations);
    }

    private void WalkList(
        OpenApiDocument document,
        IList<IOpenApiSchema>? schemas,
        string pointer,
        HashSet<OpenApiSchema> visited,
        List<ValidationViolation> violations)
    {
        if (schemas == null)
            return;

        for (var i = 0; i < schemas.Count; i++)
            Walk(document, schemas[i], $"{pointer}/{i}", visited, violations);
    }

    /// <summary>
    /// Whether every instance valid against <paramref name="schema"/> has <paramref name="property"/>.
    /// <paramref name="path"/> holds the schemas being evaluated; a schema reached again through a
    /// reference cycle proves nothing.
    /// </summary>
    private static bool Requires(OpenApiDocument document, IOpenApiSchema? schema, string property, HashSet<IOpenApiSchema> path)
    {
        var resolved = Resolve(document, schema);
        if (resolved == null || !path.Add(resolved))
            return false;

        try
        {
            if (resolved.Required?.Contains(property) == true)
                return true;

            if (resolved.AllOf?.Any(element => Requires(document, element, property, path)) == true)
                return true;

            if (resolved.OneOf is { Count: > 0 } oneOf && oneOf.All(element => Requires(document, element, property, path)))
                return true;

            return resolved.AnyOf is { Count: > 0 } anyOf && anyOf.All(element => Requires(document, element, property, path));
        }
        finally
        {
            path.Remove(resolved);
        }
    }

    /// <summary>The schema itself, or the target of a reference (resolved by the model or by component id).</summary>
    private static IOpenApiSchema? Resolve(OpenApiDocument document, IOpenApiSchema? schema)
    {
        if (schema is not OpenApiSchemaReference reference)
            return schema;

        if (reference.Target is { } target)
            return target is OpenApiSchemaReference ? Resolve(document, target) : target;

        var id = reference.Reference?.Id;
        return id != null && document.Components?.Schemas?.TryGetValue(id, out var component) == true
            ? Resolve(document, component)
            : null;
    }
}
