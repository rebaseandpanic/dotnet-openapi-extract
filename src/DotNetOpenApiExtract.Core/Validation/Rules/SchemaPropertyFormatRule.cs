using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.property-format</c>
/// The <c>format</c> of a property schema must be the one its sources promise, by the same priority
/// the generator applies: <c>[SwaggerSchema(Format)]</c>, then <c>[EmailAddress]</c> (<c>email</c>) /
/// <c>[Url]</c> (<c>uri</c>) / <c>[Phone]</c> (<c>phone</c>), then <c>[DataType]</c> by its table
/// (<c>DateTime</c> → <c>date-time</c>, <c>Date</c> → <c>date</c>, <c>Time</c> → <c>time</c>,
/// <c>Duration</c> → <c>duration</c>, <c>EmailAddress</c> → <c>email</c>, <c>Password</c> →
/// <c>password</c>, <c>Url</c> / <c>ImageUrl</c> → <c>uri</c>, <c>PhoneNumber</c> → <c>phone</c>,
/// <c>Upload</c> → <c>binary</c>), and only when none of them declares a format, the CLR type
/// (<c>Guid</c> → <c>uuid</c>, <c>DateTime</c> / <c>DateTimeOffset</c> → <c>date-time</c>,
/// <c>DateOnly</c> → <c>date</c>, <c>TimeOnly</c> → <c>time</c>; <c>Nullable&lt;T&gt;</c> unwrapped).
/// A <c>[DataType]</c> member without a format in the table, or a type outside the list, promises
/// nothing, so no format is required. With number handling the format sits on the numeric branch
/// of the <c>anyOf</c>.
/// <para>
/// Skipped in standalone mode (no CLR bindings).
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

                // The declared format wins; the type's format only stands when nothing declares one.
                var expectedFormat = Schema.SchemaGenerator.DeclaredFormat(clrProp.GetCustomAttributesData());
                if (expectedFormat == null && propType.FullName != null && ClrToFormat.TryGetValue(propType.FullName, out var fmtFromClr))
                    expectedFormat = fmtFromClr;

                if (expectedFormat == null) continue;

                var actualFormat = FormatOf(prop);
                if (!string.Equals(actualFormat, expectedFormat, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                        resolver.ForSchemaProperty(schemaId, propName),
                        $"Property '{propName}' in schema '{schemaId}' should have format='{expectedFormat}' (actual: '{actualFormat ?? "null"}').");
                }
            }
        }
    }

    /// <summary>The format of a property schema, or of the numeric branch of a number-handling <c>anyOf</c>.</summary>
    private static string? FormatOf(OpenApiSchema prop)
    {
        if (prop.Format != null)
            return prop.Format;

        var untyped = !prop.Type.HasValue || prop.Type.Value == JsonSchemaType.Null;
        return untyped && prop.AnyOf is [OpenApiSchema { Type: { } first } number, ..]
            && (first & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0
            ? number.Format
            : null;
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
