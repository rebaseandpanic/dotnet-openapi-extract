using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.property-constraints</c>
/// If a CLR property carries <c>[StringLength]</c>, <c>[Range]</c>, or <c>[RegularExpression]</c>
/// validation attributes, the corresponding schema constraints (<c>maxLength</c>, <c>minimum</c>,
/// <c>maximum</c>, <c>pattern</c>) must be present.
/// <para>
/// Skipped in standalone mode (no CLR bindings available).
/// </para>
/// </summary>
public sealed class SchemaPropertyConstraintsRule : IValidationRule
{
    public string Id => "schema.property-constraints";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    private const string StringLengthAttr    = "System.ComponentModel.DataAnnotations.StringLengthAttribute";
    private const string MaxLengthAttr       = "System.ComponentModel.DataAnnotations.MaxLengthAttribute";
    private const string MinLengthAttr       = "System.ComponentModel.DataAnnotations.MinLengthAttribute";
    private const string RangeAttr           = "System.ComponentModel.DataAnnotations.RangeAttribute";
    private const string RegexAttr           = "System.ComponentModel.DataAnnotations.RegularExpressionAttribute";

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        if (context.TypeBySchemaId == null) yield break; // standalone mode — skip
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s || s.Properties == null) continue;
            if (!context.TypeBySchemaId.TryGetValue(schemaId, out var clrType)) continue;

            foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key))
            {
                if (propSchema is not OpenApiSchema prop) continue;

                // Find CLR property (case-insensitive fallback)
                var clrProp = clrType.GetProperties(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase));

                if (clrProp == null) continue;

                var attrs = clrProp.GetCustomAttributesData();

                foreach (var attr in attrs)
                {
                    var fullName = attr.AttributeType.FullName;
                    if (fullName == null) continue;

                    if (fullName == StringLengthAttr || fullName == MaxLengthAttr)
                    {
                        if (prop.MaxLength == null)
                        {
                            yield return new ValidationViolation(
                                Id,
                                DefaultSeverity,
                                JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                                resolver.ForSchemaProperty(schemaId, propName),
                                $"Property '{propName}' in '{schemaId}' has [{attr.AttributeType.Name}] but schema lacks 'maxLength'.");
                        }
                    }
                    else if (fullName == MinLengthAttr)
                    {
                        if (prop.MinLength == null)
                        {
                            yield return new ValidationViolation(
                                Id,
                                DefaultSeverity,
                                JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                                resolver.ForSchemaProperty(schemaId, propName),
                                $"Property '{propName}' in '{schemaId}' has [{attr.AttributeType.Name}] but schema lacks 'minLength'.");
                        }
                    }
                    else if (fullName == RangeAttr)
                    {
                        if (prop.Minimum == null && prop.Maximum == null)
                        {
                            yield return new ValidationViolation(
                                Id,
                                DefaultSeverity,
                                JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                                resolver.ForSchemaProperty(schemaId, propName),
                                $"Property '{propName}' in '{schemaId}' has [Range] but schema lacks 'minimum' and 'maximum'.");
                        }
                    }
                    else if (fullName == RegexAttr)
                    {
                        if (string.IsNullOrEmpty(prop.Pattern))
                        {
                            yield return new ValidationViolation(
                                Id,
                                DefaultSeverity,
                                JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                                resolver.ForSchemaProperty(schemaId, propName),
                                $"Property '{propName}' in '{schemaId}' has [RegularExpression] but schema lacks 'pattern'.");
                        }
                    }
                }
            }
        }
    }
}
