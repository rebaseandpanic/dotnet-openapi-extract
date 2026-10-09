using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>parameter.schema-type</c>
/// Every operation parameter must have a schema with a non-null/non-empty type.
/// </summary>
public sealed class ParameterSchemaTypeRule : IValidationRule
{
    public string Id => "parameter.schema-type";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (operation.Parameters == null) continue;

            foreach (var param in operation.Parameters.OfType<OpenApiParameter>().OrderBy(p => p.Name))
            {
                // Skip $ref parameters — they get their type from the referenced schema
                if (param.Schema == null || (param.Schema is OpenApiSchema schema && schema.Type == JsonSchemaType.Null))
                {
                    // Only flag if no schema at all, or schema has no type
                    bool noType = param.Schema == null ||
                        (param.Schema is OpenApiSchema s && s.Type == JsonSchemaType.Null && s.AnyOf == null && s.OneOf == null && s.AllOf == null);

                    if (noType)
                    {
                        var key = op.Key;
                        yield return new ValidationViolation(
                            Id,
                            DefaultSeverity,
                            JsonPointerHelper.ForParameterOf(op.Pointer, param.Name ?? "?"),
                            resolver.ForOperation(key),
                            $"Parameter '{param.Name}' schema has no type defined.");
                    }
                }
            }
        }
    }
}
