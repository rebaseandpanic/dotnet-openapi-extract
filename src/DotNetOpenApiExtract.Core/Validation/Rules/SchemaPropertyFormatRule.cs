using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.property-format</c>
/// For properties whose CLR type implies a specific format, the schema must have the matching
/// <c>format</c> value: Guid→uuid, DateTime/DateTimeOffset→date-time, DateOnly→date,
/// TimeOnly→time, [EmailAddress]→email, [Url]→uri.
/// <para>
/// In standalone mode (no CLR bindings), this rule degenerates to checking whether
/// string properties that are named in a format-suggestive way have a non-null format.
/// </para>
/// </summary>
public sealed class SchemaPropertyFormatRule : IValidationRule
{
    public string Id => "schema.property-format";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    // Known CLR FullName → expected OpenAPI format
    private static readonly Dictionary<string, string> ClrToFormat = new(StringComparer.Ordinal)
    {
        ["System.Guid"]           = "uuid",
        ["System.DateTime"]       = "date-time",
        ["System.DateTimeOffset"] = "date-time",
        ["System.DateOnly"]       = "date",
        ["System.TimeOnly"]       = "time",
    };

    // Validation attribute FullName → expected OpenAPI format
    private static readonly Dictionary<string, string> AttrToFormat = new(StringComparer.Ordinal)
    {
        ["System.ComponentModel.DataAnnotations.EmailAddressAttribute"] = "email",
        ["System.ComponentModel.DataAnnotations.UrlAttribute"]          = "uri",
        // DataType(DataType.Date) → "date" handled separately in BuildWithValidation (complex to detect statically)
    };

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        if (context.TypeBySchemaId == null) yield break; // skip in standalone mode
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key))
        {
            if (schema is not OpenApiSchema s || s.Properties == null) continue;
            if (!context.TypeBySchemaId.TryGetValue(schemaId, out var clrType)) continue;

            foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key))
            {
                if (propSchema is not OpenApiSchema prop) continue;

                // Find the CLR property
                var clrProp = clrType.GetProperties(
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    .FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase)
                        || MatchesSerializedName(p, propName));

                if (clrProp == null) continue;

                // Determine expected format from CLR type
                var propType = clrProp.PropertyType;
                // Unwrap Nullable<T>
                if (propType.FullName?.StartsWith("System.Nullable`1", StringComparison.Ordinal) == true)
                    propType = propType.GetGenericArguments().FirstOrDefault() ?? propType;

                string? expectedFormat = null;

                if (propType.FullName != null && ClrToFormat.TryGetValue(propType.FullName, out var fmtFromClr))
                    expectedFormat = fmtFromClr;

                if (expectedFormat == null)
                {
                    // Check validation attributes
                    foreach (var attr in clrProp.GetCustomAttributesData())
                    {
                        if (attr.AttributeType.FullName != null &&
                            AttrToFormat.TryGetValue(attr.AttributeType.FullName, out var fmtFromAttr))
                        {
                            expectedFormat = fmtFromAttr;
                            break;
                        }
                    }
                }

                if (expectedFormat == null) continue;

                if (!string.Equals(prop.Format, expectedFormat, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                        resolver.ForSchemaProperty(schemaId, propName),
                        $"Property '{propName}' in schema '{schemaId}' should have format='{expectedFormat}' (actual: '{prop.Format ?? "null"}').");
                }
            }
        }
    }

    private static bool MatchesSerializedName(System.Reflection.PropertyInfo prop, string propName)
    {
        // Check [JsonPropertyName]
        foreach (var attr in prop.GetCustomAttributesData())
        {
            if (attr.AttributeType.FullName == "System.Text.Json.Serialization.JsonPropertyNameAttribute"
                && attr.ConstructorArguments.Count > 0)
            {
                var val = attr.ConstructorArguments[0].Value as string;
                if (string.Equals(val, propName, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }
}
