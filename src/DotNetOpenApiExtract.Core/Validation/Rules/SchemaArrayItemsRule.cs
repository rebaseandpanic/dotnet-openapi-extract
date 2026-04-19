using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.array-items</c>
/// Schemas with <c>type: array</c> must declare an <c>items</c> field.
/// Without <c>items</c>, code generators produce <c>List&lt;object&gt;</c> at best;
/// many tools reject the schema outright. Spectral <c>array-items</c> severity 0.
/// </summary>
public sealed class SchemaArrayItemsRule : IValidationRule
{
    public string Id => "schema.array-items";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        // Walk component schemas
        if (document.Components?.Schemas != null)
        {
            foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
            {
                if (schema is not OpenApiSchema s) continue;
                foreach (var v in WalkSchema(s, JsonPointerHelper.ForSchema(schemaId)))
                    yield return v;
            }
        }

        // Walk inline schemas on operations (parameters, request body, responses)
        if (document.Paths != null)
        {
            foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
            {
                if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

                foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
                {
                    var opPtr = JsonPointerHelper.ForOperation(path, method.ToString());

                    // Parameters
                    if (operation.Parameters != null)
                    {
                        for (int i = 0; i < operation.Parameters.Count; i++)
                        {
                            var param = operation.Parameters[i];
                            if (param?.Schema is OpenApiSchema ps)
                            {
                                foreach (var v in WalkSchema(ps, $"{opPtr}/parameters/{i}/schema"))
                                    yield return v;
                            }
                        }
                    }

                    // Request body
                    if (operation.RequestBody?.Content != null)
                    {
                        foreach (var (mt, mediaType) in operation.RequestBody.Content)
                        {
                            if (mediaType?.Schema is OpenApiSchema rbs)
                            {
                                var ptr = $"{opPtr}/requestBody/content/{JsonPointerHelper.EncodeSegment(mt)}/schema";
                                foreach (var v in WalkSchema(rbs, ptr))
                                    yield return v;
                            }
                        }
                    }

                    // Responses
                    if (operation.Responses != null)
                    {
                        foreach (var (status, response) in operation.Responses.OrderBy(kv => kv.Key))
                        {
                            if (response?.Content == null) continue;
                            foreach (var (mt, mediaType) in response.Content)
                            {
                                if (mediaType?.Schema is OpenApiSchema rs)
                                {
                                    var ptr = $"{opPtr}/responses/{status}/content/{JsonPointerHelper.EncodeSegment(mt)}/schema";
                                    foreach (var v in WalkSchema(rs, ptr))
                                        yield return v;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private IEnumerable<ValidationViolation> WalkSchema(OpenApiSchema schema, string pointer)
    {
        if (schema.Type == JsonSchemaType.Array && schema.Items == null)
        {
            yield return new ValidationViolation(
                Id,
                DefaultSeverity,
                pointer,
                null,
                $"Array schema at '{pointer}' has no 'items' definition.");
        }

        // Recurse into properties
        if (schema.Properties != null)
        {
            foreach (var (propName, propSchema) in schema.Properties.OrderBy(kv => kv.Key))
            {
                if (propSchema is OpenApiSchema ps)
                {
                    var propPtr = $"{pointer}/properties/{JsonPointerHelper.EncodeSegment(propName)}";
                    foreach (var v in WalkSchema(ps, propPtr))
                        yield return v;
                }
            }
        }

        // Recurse into items
        if (schema.Items is OpenApiSchema itemSchema)
        {
            foreach (var v in WalkSchema(itemSchema, $"{pointer}/items"))
                yield return v;
        }

        // Recurse into anyOf/allOf/oneOf
        if (schema.AnyOf != null)
        {
            for (int i = 0; i < schema.AnyOf.Count; i++)
            {
                if (schema.AnyOf[i] is OpenApiSchema s)
                    foreach (var v in WalkSchema(s, $"{pointer}/anyOf/{i}"))
                        yield return v;
            }
        }

        if (schema.AllOf != null)
        {
            for (int i = 0; i < schema.AllOf.Count; i++)
            {
                if (schema.AllOf[i] is OpenApiSchema s)
                    foreach (var v in WalkSchema(s, $"{pointer}/allOf/{i}"))
                        yield return v;
            }
        }

        if (schema.OneOf != null)
        {
            for (int i = 0; i < schema.OneOf.Count; i++)
            {
                if (schema.OneOf[i] is OpenApiSchema s)
                    foreach (var v in WalkSchema(s, $"{pointer}/oneOf/{i}"))
                        yield return v;
            }
        }
    }
}
