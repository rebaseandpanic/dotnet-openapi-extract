using System.Globalization;
using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>schema.property-constraints</c>
/// The validation attributes of a CLR property must appear in its schema, with a bound at least as
/// tight as the attribute's:
/// <list type="bullet">
/// <item><c>[StringLength(max, MinimumLength = min)]</c> → <c>maxLength</c> / <c>minLength</c>.</item>
/// <item><c>[MaxLength]</c>, <c>[MinLength]</c>, <c>[Length(min, max)]</c> → by the shape of the schema:
/// <c>maxItems</c> / <c>minItems</c> for an array, <c>maxProperties</c> / <c>minProperties</c> for a
/// dictionary (an object with <c>additionalProperties</c>), <c>maxLength</c> / <c>minLength</c>
/// otherwise. A bound of 0 (or a declaration <c>LengthAttribute</c> rejects) constrains nothing.</item>
/// <item><c>[Range]</c>, every constructor (<c>int</c>, <c>double</c>, <c>(Type, string, string)</c>)
/// read as <c>RangeAttribute</c> reads it: an inclusive side needs <c>minimum</c> / <c>maximum</c>
/// equal to the bound, an exclusive side (<c>MinimumIsExclusive</c> / <c>MaximumIsExclusive</c>)
/// needs <c>exclusiveMinimum</c> / <c>exclusiveMaximum</c> equal to it (the model holds the number;
/// OpenAPI 3.0 output writes it as the bound plus a boolean). The numeric schema is the property
/// schema itself — nullable included — or the numeric branch of a number-handling <c>anyOf</c>. A
/// non-numeric schema or operand, a bound JSON cannot hold (infinity, NaN) and a declaration
/// <c>RangeAttribute</c> rejects expect nothing.</item>
/// <item><c>[RegularExpression]</c> → <c>pattern</c>.</item>
/// </list>
/// <para>
/// Skipped in standalone mode (no CLR bindings available).
/// </para>
/// </summary>
public sealed class SchemaPropertyConstraintsRule : IValidationRule
{
    public string Id => "schema.property-constraints";
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

            foreach (var (propName, propSchema) in s.Properties.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (propSchema is not OpenApiSchema prop) continue;

                // Find CLR property (case-insensitive fallback)
                var clrProp = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(p => string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase));
                if (clrProp == null) continue;

                foreach (var problem in Check(prop, clrProp.GetCustomAttributesData()))
                {
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForSchemaProperty(schemaId, propName),
                        resolver.ForSchemaProperty(schemaId, propName),
                        $"Property '{propName}' in '{schemaId}' {problem}.");
                }
            }
        }
    }

    /// <summary>One text per constraint of <paramref name="attributes"/> the schema does not express.</summary>
    private static IEnumerable<string> Check(OpenApiSchema prop, IList<CustomAttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            var name = attr.AttributeType.Name;
            switch (attr.AttributeType.FullName)
            {
                case AttributeHelper.Names.StringLength:
                {
                    // [StringLength] is string-only by contract — always maps to maxLength / minLength.
                    var max = AttributeHelper.GetConstructorArgument<int>(attr, 0);
                    var min = AttributeHelper.GetNamedArgument<int>(attr, "MinimumLength");
                    if (max > 0 && AtMost(prop.MaxLength, max, "maxLength", name) is { } maxProblem) yield return maxProblem;
                    if (min > 0 && AtLeast(prop.MinLength, min, "minLength", name) is { } minProblem) yield return minProblem;
                    break;
                }

                case AttributeHelper.Names.MaxLength:
                {
                    var max = AttributeHelper.GetConstructorArgument<int>(attr, 0);
                    if (max > 0 && MaximumLength(prop, max, name) is { } problem) yield return problem;
                    break;
                }

                case AttributeHelper.Names.MinLength:
                {
                    var min = AttributeHelper.GetConstructorArgument<int>(attr, 0);
                    if (min > 0 && MinimumLength(prop, min, name) is { } problem) yield return problem;
                    break;
                }

                case AttributeHelper.Names.Length:
                {
                    var min = AttributeHelper.GetConstructorArgument<int>(attr, 0);
                    var max = AttributeHelper.GetConstructorArgument<int>(attr, 1);
                    if (min < 0 || max < min) break; // LengthAttribute rejects it: constrains nothing
                    if (min > 0 && MinimumLength(prop, min, name) is { } minProblem) yield return minProblem;
                    if (MaximumLength(prop, max, name) is { } maxProblem) yield return maxProblem;
                    break;
                }

                case AttributeHelper.Names.Range:
                    foreach (var problem in CheckRange(prop, attr))
                        yield return problem;
                    break;

                case AttributeHelper.Names.RegularExpression:
                    if (string.IsNullOrEmpty(prop.Pattern))
                        yield return $"has [{name}] but schema lacks 'pattern'";
                    break;
            }
        }
    }

    private static string? MaximumLength(OpenApiSchema prop, int max, string attribute) => Shape(prop) switch
    {
        LengthShape.Array      => AtMost(prop.MaxItems, max, "maxItems", attribute),
        LengthShape.Dictionary => AtMost(prop.MaxProperties, max, "maxProperties", attribute),
        _                      => AtMost(prop.MaxLength, max, "maxLength", attribute),
    };

    private static string? MinimumLength(OpenApiSchema prop, int min, string attribute) => Shape(prop) switch
    {
        LengthShape.Array      => AtLeast(prop.MinItems, min, "minItems", attribute),
        LengthShape.Dictionary => AtLeast(prop.MinProperties, min, "minProperties", attribute),
        _                      => AtLeast(prop.MinLength, min, "minLength", attribute),
    };

    private enum LengthShape { Text, Array, Dictionary }

    /// <summary>Which keywords a length attribute maps to, by the same rule the generator uses.</summary>
    private static LengthShape Shape(OpenApiSchema prop)
    {
        if (prop.Type.HasValue && prop.Type.Value.HasFlag(JsonSchemaType.Array))
            return LengthShape.Array;
        if (prop.Type.HasValue && prop.Type.Value.HasFlag(JsonSchemaType.Object) && prop.AdditionalProperties != null)
            return LengthShape.Dictionary;
        return LengthShape.Text;
    }

    private static string? AtMost(int? actual, int bound, string keyword, string attribute) => actual switch
    {
        null              => $"has [{attribute}] but schema lacks '{keyword}'",
        { } a when a > bound => $"has [{attribute}] with {bound} but schema has '{keyword}' {a}",
        _                 => null,
    };

    private static string? AtLeast(int? actual, int bound, string keyword, string attribute) => actual switch
    {
        null              => $"has [{attribute}] but schema lacks '{keyword}'",
        { } a when a < bound => $"has [{attribute}] with {bound} but schema has '{keyword}' {a}",
        _                 => null,
    };

    private static IEnumerable<string> CheckRange(OpenApiSchema prop, CustomAttributeData attribute)
    {
        if (RangeDeclaration.Read(attribute) is not { Kind: RangeDeclaration.Outcome.Numeric } range)
            yield break;

        if (NumericSchema(prop) is not { } number)
            yield break; // a range constrains only a numeric schema

        if (range.Minimum != null
            && Side(range.Minimum, range.MinimumIsExclusive, number.Minimum, number.ExclusiveMinimum, "minimum", "exclusiveMinimum") is { } minProblem)
            yield return minProblem;

        if (range.Maximum != null
            && Side(range.Maximum, range.MaximumIsExclusive, number.Maximum, number.ExclusiveMaximum, "maximum", "exclusiveMaximum") is { } maxProblem)
            yield return maxProblem;
    }

    private static string? Side(string bound, bool exclusive, string? inclusiveValue, string? exclusiveValue, string inclusiveKeyword, string exclusiveKeyword)
    {
        var (keyword, actual) = exclusive ? (exclusiveKeyword, exclusiveValue) : (inclusiveKeyword, inclusiveValue);
        var kind = exclusive ? "an exclusive" : "an inclusive";
        if (actual == null)
            return $"has [Range] with {kind} bound {bound} but schema lacks '{keyword}'";
        if (!SameNumber(actual, bound))
            return $"has [Range] with {kind} bound {bound} but schema has '{keyword}' {actual}";
        return null;
    }

    /// <summary>
    /// The schema a range constrains: the property schema when it is numeric (nullable or not), or the
    /// numeric first branch of a number-handling <c>anyOf</c> (untyped, or <c>null</c>-typed in its
    /// nullable 3.0 form).
    /// </summary>
    private static OpenApiSchema? NumericSchema(OpenApiSchema prop)
    {
        if (IsNumeric(prop.Type))
            return prop;

        if ((!prop.Type.HasValue || prop.Type.Value == JsonSchemaType.Null)
            && prop.AnyOf is [OpenApiSchema first, ..] && IsNumeric(first.Type))
            return first;

        return null;
    }

    private static bool IsNumeric(JsonSchemaType? type) =>
        type.HasValue && (type.Value & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0;

    /// <summary>Two JSON number lexemes denote the same number (exactly as decimals, else as doubles).</summary>
    private static bool SameNumber(string left, string right)
    {
        const NumberStyles Style = NumberStyles.Float;
        if (decimal.TryParse(left, Style, CultureInfo.InvariantCulture, out var l)
            && decimal.TryParse(right, Style, CultureInfo.InvariantCulture, out var r))
            return l == r;
        if (double.TryParse(left, Style, CultureInfo.InvariantCulture, out var ld)
            && double.TryParse(right, Style, CultureInfo.InvariantCulture, out var rd))
            return ld.Equals(rd);
        return string.Equals(left, right, StringComparison.Ordinal);
    }
}
