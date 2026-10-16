using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.required-consistency</c>
/// A property the CLR type declares required — <c>[Required]</c>, <c>[JsonRequired]</c> or the C#
/// <c>required</c> modifier — must be listed in its schema's <c>required</c> array.
/// <para>
/// A non-nullable value type is not a declaration: System.Text.Json reads its default when the
/// member is absent, so the generator leaves it optional (as Swashbuckle does), and the rule does not
/// ask for it. Nor is a non-nullable reference type: its requirement depends on the serializer
/// options the document was built with.
/// </para>
/// <para>
/// Skipped in standalone mode (no CLR bindings available): a schema alone does not say what the
/// code declares.
/// </para>
/// </summary>
public sealed class SchemaRequiredConsistencyRule : IValidationRule
{
    public string Id => "schema.required-consistency";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        if (document.Components?.Schemas == null) yield break;
        if (context.TypeBySchemaId == null) yield break; // standalone mode — skip
        var resolver = new ViolationLocationResolver(context);

        foreach (var (schemaId, schema) in document.Components.Schemas.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (schema is not OpenApiSchema s || s.Properties == null) continue;
            if (!context.TypeBySchemaId.TryGetValue(schemaId, out var clrType)) continue;

            var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var propName in s.Properties.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (s.Required?.Contains(propName) == true) continue;
                if (ClrProperty(properties, propName) is not { } clrProp) continue;
                if (!IsDeclaredRequired(clrProp.GetCustomAttributesData())) continue;

                yield return new ValidationViolation(
                    Id,
                    DefaultSeverity,
                    JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                    resolver.ForSchemaProperty(schemaId, propName),
                    $"Property '{propName}' in schema '{schemaId}' is declared required by '{clrType.Name}.{clrProp.Name}' but is not listed in the 'required' array.");
            }
        }
    }

    /// <summary>
    /// The CLR property written as <paramref name="propName"/>: the one whose <c>[JsonPropertyName]</c>
    /// is that name, else the one with that name ignoring case (the naming policy).
    /// </summary>
    private static PropertyInfo? ClrProperty(PropertyInfo[] properties, string propName) =>
        properties.FirstOrDefault(p => JsonName(p) == propName)
        ?? properties.FirstOrDefault(p => JsonName(p) == null && string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase));

    private static string? JsonName(PropertyInfo property) =>
        AttributeHelper.GetAttribute(property.GetCustomAttributesData(), AttributeHelper.Names.JsonPropertyName) is { } attr
            ? AttributeHelper.GetConstructorArgument<string>(attr, 0)
            : null;

    private static bool IsDeclaredRequired(IList<CustomAttributeData> attributes) =>
        AttributeHelper.HasAttribute(attributes, AttributeHelper.Names.Required)
        || AttributeHelper.HasAttribute(attributes, AttributeHelper.Names.JsonRequired)
        || AttributeHelper.HasAttribute(attributes, AttributeHelper.Names.RequiredMember);
}
