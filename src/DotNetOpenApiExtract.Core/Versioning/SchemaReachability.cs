using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>
/// Which component schemas the output can reach: from operations and path items (paths and
/// webhooks) through every schema position and every <c>$ref</c>, transitively through components.
/// A component reached only by excluded operations is not reachable.
/// </summary>
internal static class SchemaReachability
{
    /// <summary>Component ids reachable from the paths and webhooks of <paramref name="document"/>.</summary>
    public static IReadOnlySet<string> FromDocument(OpenApiDocument document)
    {
        var roots = new List<string>();
        foreach (var pathItem in (document.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
                     .Concat(document.Webhooks?.Values ?? Enumerable.Empty<IOpenApiPathItem>()))
        {
            CollectPathItem(pathItem, roots);
        }

        return Close(roots, document.Components?.Schemas);
    }

    /// <summary>Component ids reachable from <paramref name="roots"/> within <paramref name="schemas"/>.</summary>
    public static IReadOnlySet<string> FromSchemas(IEnumerable<IOpenApiSchema> roots, IReadOnlyDictionary<string, IOpenApiSchema> schemas)
    {
        var ids = new List<string>();
        foreach (var root in roots)
            CollectSchema(root, ids);
        return Close(ids, schemas);
    }

    private static HashSet<string> Close(IEnumerable<string> roots, IEnumerable<KeyValuePair<string, IOpenApiSchema>>? components)
    {
        var lookup = components?.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal)
                     ?? new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(roots);
        while (pending.TryDequeue(out var id))
        {
            if (!reached.Add(id) || !lookup.TryGetValue(id, out var schema))
                continue;

            var nested = new List<string>();
            CollectSchema(schema, nested);
            foreach (var next in nested)
                pending.Enqueue(next);
        }

        return reached;
    }

    private static void CollectPathItem(IOpenApiPathItem pathItem, List<string> ids)
    {
        foreach (var parameter in pathItem.Parameters ?? [])
            CollectParameter(parameter, ids);

        foreach (var (_, operation) in pathItem.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
        {
            foreach (var parameter in operation.Parameters ?? [])
                CollectParameter(parameter, ids);

            CollectContent(operation.RequestBody?.Content, ids);

            foreach (var (_, response) in operation.Responses ?? new OpenApiResponses())
            {
                CollectContent(response.Content, ids);
                foreach (var (_, header) in response.Headers ?? new Dictionary<string, IOpenApiHeader>())
                {
                    CollectSchema(header.Schema, ids);
                    CollectContent(header.Content, ids);
                }
            }
        }
    }

    private static void CollectParameter(IOpenApiParameter? parameter, List<string> ids)
    {
        if (parameter == null)
            return;
        CollectSchema(parameter.Schema, ids);
        CollectContent(parameter.Content, ids);
    }

    private static void CollectContent(IDictionary<string, IOpenApiMediaType>? content, List<string> ids)
    {
        foreach (var (_, mediaType) in content ?? new Dictionary<string, IOpenApiMediaType>())
        {
            CollectSchema(mediaType.Schema, ids);
            CollectSchema(mediaType.ItemSchema, ids);
        }
    }

    /// <summary>Ids referenced by <paramref name="schema"/>: a reference yields its id, other schemas are walked.</summary>
    private static void CollectSchema(IOpenApiSchema? schema, List<string> ids)
    {
        switch (schema)
        {
            case null:
                return;

            case OpenApiSchemaReference reference:
                if (reference.Reference.Id is { } id)
                    ids.Add(id);
                return;
        }

        foreach (var (_, property) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
            CollectSchema(property, ids);
        foreach (var (_, property) in schema.PatternProperties ?? new Dictionary<string, IOpenApiSchema>())
            CollectSchema(property, ids);
        foreach (var part in (schema.AllOf ?? []).Concat(schema.AnyOf ?? []).Concat(schema.OneOf ?? []))
            CollectSchema(part, ids);

        CollectSchema(schema.Items, ids);
        CollectSchema(schema.Not, ids);
        CollectSchema(schema.AdditionalProperties, ids);
        if (schema is OpenApiSchema concrete)
        {
            // Only on the concrete model type in Microsoft.OpenApi 3.10.2.
            CollectSchema(concrete.PropertyNames, ids);
            CollectSchema(concrete.ContentSchema, ids);
        }

        foreach (var (_, target) in schema.Discriminator?.Mapping ?? new Dictionary<string, OpenApiSchemaReference>())
            CollectSchema(target, ids);
        if (schema.Discriminator?.DefaultMapping is { } defaultMapping)
            CollectSchema(defaultMapping, ids);
    }
}
