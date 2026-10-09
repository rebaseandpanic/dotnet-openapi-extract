using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>parameter.optional-has-default</c>
/// Optional parameters (Required == false) whose schema is a value type (non-nullable or nullable
/// wrapping a non-object type) should have a <c>default</c> value specified.
/// Ref-type parameters (string, arrays, objects) are skipped since null is an acceptable default.
/// </summary>
public sealed class ParameterOptionalHasDefaultRule : IValidationRule
{
    public string Id => "parameter.optional-has-default";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Warning;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        var resolver = new ViolationLocationResolver(context);

        foreach (var op in OperationEnumerator.Enumerate(document, context.OpenApiSpecVersion))
        {
            var operation = op.Operation;
            if (operation.Parameters == null) continue;

            foreach (var param in operation.Parameters.OfType<OpenApiParameter>().OrderBy(p => p.Name))
            {
                if (param.Required) continue;
                if (param.Schema is not OpenApiSchema schema) continue;

                // Only flag value types: integer, number, boolean (not string, object, array)
                bool isValueType = schema.Type == JsonSchemaType.Integer
                    || schema.Type == JsonSchemaType.Number
                    || schema.Type == JsonSchemaType.Boolean;

                if (!isValueType) continue;

                // Check if schema has a default value
                bool hasDefault = schema.Default != null;
                if (!hasDefault)
                {
                    var key = op.Key;
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForParameterOf(op.Pointer, param.Name ?? "?"),
                        resolver.ForOperation(key),
                        $"Optional value-type parameter '{param.Name}' has no default value specified.");
                }
            }
        }
    }
}
