using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.required-consistency</c>
/// For object schemas in <c>components/schemas</c>: non-nullable properties that are not in
/// the schema's <c>required</c> array are flagged as potential inconsistencies.
/// A property is considered non-nullable when its schema has no nullable type hint
/// (<c>type</c> is not a nullable union) and it is a value type or has no <c>x-nullable</c> extension.
/// </summary>
public sealed class SchemaRequiredConsistencyRule : IValidationRule
{
    public string Id => "schema.required-consistency";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s || s.Properties == null) continue;

            var requiredSet = s.Required != null
                ? new HashSet<string>(s.Required, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key))
            {
                if (requiredSet.Contains(propName)) continue;
                if (propSchema is not OpenApiSchema prop) continue;

                // Consider non-nullable: single type (no nullable union), not nullable type
                bool isNonNullable = IsNonNullableSchema(prop);
                if (!isNonNullable) continue;

                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                    resolver.ForSchemaProperty(schemaId, propName),
                    $"Non-nullable property '{propName}' in schema '{schemaId}' is not listed in the 'required' array.");
            }
        }
    }

    private static bool IsNonNullableSchema(OpenApiSchema schema)
    {
        // Value types — always non-nullable unless wrapped in Nullable<T>
        if (schema.Type == JsonSchemaType.Integer
            || schema.Type == JsonSchemaType.Number
            || schema.Type == JsonSchemaType.Boolean)
            return true;

        // String with NRT non-nullable: type is String only (no Null flag).
        // A nullable string is encoded as String | Null (OpenAPI 3.1 union).
        if (schema.Type == JsonSchemaType.String)
            return true;

        // Object/array — skip
        return false;
    }
}
