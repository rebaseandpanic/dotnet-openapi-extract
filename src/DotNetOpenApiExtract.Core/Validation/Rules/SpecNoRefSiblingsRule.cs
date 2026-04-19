using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>spec.no-ref-siblings</c>
/// In OpenAPI 3.0, a <c>$ref</c> object must not have sibling properties — they are silently
/// ignored by parsers, causing data loss. In OAS 3.1+ this restriction is lifted (JSON Schema
/// Draft 2020-12 allows <c>$ref</c> with siblings), so the rule skips when the document
/// targets 3.1 or 3.2.
/// <para>
/// Implementation note: Microsoft.OpenApi 3.5 represents references as typed reference-holder
/// objects (<see cref="OpenApiSchemaReference"/>, <see cref="OpenApiResponseReference"/>, etc.)
/// and does not retain raw sibling properties from JSON parsing in the same way a raw parser
/// would. As a result this rule inspects reference holders that carry locally-specified
/// overriding content (Description, Summary, Title, Extensions) beyond the target definition —
/// situations that are legal in 3.1 but illegal in 3.0. The rule will fire less often than a
/// raw-JSON walker would but covers the representable cases.
/// </para>
/// <para>
/// Version gating: when <see cref="ValidationContext.OpenApiSpecVersion"/> is
/// <see cref="Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1"/> or
/// <see cref="Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_2"/> the rule emits no violations.
/// When the version is null (unknown), the rule applies conservatively (emits violations).
/// Users targeting 3.1+ without setting the version can suppress via <c>--skip-rule spec.no-ref-siblings</c>.
/// </para>
/// </summary>
public sealed class SpecNoRefSiblingsRule : IValidationRule
{
    public string Id => "spec.no-ref-siblings";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        // Rule applies only to OpenAPI 3.0. In 3.1+, JSON Schema Draft 2020-12 allows
        // $ref with siblings. If the version is unknown (null), be conservative and emit
        // violations; users can suppress via --skip-rule if they know they're on 3.1+.
        if (context.OpenApiSpecVersion == Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1
            || context.OpenApiSpecVersion == Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_2)
        {
            yield break;
        }

        if (document.Components?.Schemas == null && document.Paths == null)
            yield break;

        // Walk all schema references in components
        if (document.Components?.Schemas != null)
        {
            foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
            {
                if (schema is OpenApiSchemaReference refSchema)
                {
                    if (HasSiblingContent(refSchema))
                    {
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            $"#/components/schemas/{JsonPointerHelper.EncodeSegment(schemaId)}",
                            null,
                            $"Schema '{schemaId}' is a $ref with sibling properties, which is not allowed in OpenAPI 3.0.");
                    }
                }
                else if (schema is OpenApiSchema s)
                {
                    foreach (var v in WalkSchemaForRefSiblings(s, $"#/components/schemas/{JsonPointerHelper.EncodeSegment(schemaId)}"))
                        yield return v;
                }
            }
        }

        // Walk schemas used in paths/operations
        if (document.Paths != null)
        {
            foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
            {
                if (pathItem is not OpenApiPathItem item) continue;
                if (item.Operations == null) continue;

                foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
                {
                    var opPtr = JsonPointerHelper.ForOperation(path, method.ToString());

                    // Parameters
                    if (operation.Parameters != null)
                    {
                        for (int i = 0; i < operation.Parameters.Count; i++)
                        {
                            var param = operation.Parameters[i];
                            if (param?.Schema is OpenApiSchemaReference pRef && HasSiblingContent(pRef))
                            {
                                yield return new ValidationViolation(
                                    Id, DefaultSeverity,
                                    $"{opPtr}/parameters/{i}/schema",
                                    null,
                                    "Parameter schema is a $ref with sibling properties, not allowed in OpenAPI 3.0.");
                            }
                        }
                    }

                    // Request body
                    if (operation.RequestBody?.Content != null)
                    {
                        foreach (var (mt, mediaType) in operation.RequestBody.Content)
                        {
                            if (mediaType?.Schema is OpenApiSchemaReference rbRef && HasSiblingContent(rbRef))
                            {
                                yield return new ValidationViolation(
                                    Id, DefaultSeverity,
                                    $"{opPtr}/requestBody/content/{JsonPointerHelper.EncodeSegment(mt)}/schema",
                                    null,
                                    "Request body schema is a $ref with sibling properties, not allowed in OpenAPI 3.0.");
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
                                if (mediaType?.Schema is OpenApiSchemaReference rRef && HasSiblingContent(rRef))
                                {
                                    yield return new ValidationViolation(
                                        Id, DefaultSeverity,
                                        $"{opPtr}/responses/{status}/content/{JsonPointerHelper.EncodeSegment(mt)}/schema",
                                        null,
                                        "Response schema is a $ref with sibling properties, not allowed in OpenAPI 3.0.");
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static bool HasSiblingContent(OpenApiSchemaReference refSchema)
    {
        // A $ref with sibling content is detectable when the reference holder has
        // locally-overriding fields that should not co-exist with $ref in OAS 3.0.
        return !string.IsNullOrEmpty(refSchema.Description)
            || !string.IsNullOrEmpty(refSchema.Title)
            || (refSchema.Extensions != null && refSchema.Extensions.Count > 0);
    }

    private IEnumerable<ValidationViolation> WalkSchemaForRefSiblings(OpenApiSchema schema, string pointer)
    {
        if (schema.Properties != null)
        {
            foreach (var (propName, propSchema) in schema.Properties.OrderBy(kv => kv.Key))
            {
                var propPtr = $"{pointer}/properties/{JsonPointerHelper.EncodeSegment(propName)}";
                if (propSchema is OpenApiSchemaReference pRef && HasSiblingContent(pRef))
                {
                    yield return new ValidationViolation(
                        Id, DefaultSeverity, propPtr, null,
                        $"Property '{propName}' schema is a $ref with sibling properties, not allowed in OpenAPI 3.0.");
                }
                else if (propSchema is OpenApiSchema ps)
                {
                    foreach (var v in WalkSchemaForRefSiblings(ps, propPtr))
                        yield return v;
                }
            }
        }
    }
}
