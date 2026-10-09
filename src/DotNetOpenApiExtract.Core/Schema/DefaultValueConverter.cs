using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Turns a <c>[DefaultValue]</c> declaration (or a C# parameter default) into the JSON value of
/// <c>default</c>, one contract for DTO properties and action parameters.
/// </summary>
/// <remarks>
/// <c>[DefaultValue(Type, string)]</c> is converted to the type in the invariant culture: numbers
/// become JSON numbers, <c>bool</c> a boolean, <c>Guid</c> a string, dates and times the string
/// System.Text.Json writes for them (RFC 3339), an enum its name or number by the schema's form.
/// A type the converter does not know gives no value, with a reason.
/// A literal (<c>[DefaultValue(5)]</c>, <c>int page = 1</c>) keeps its JSON type.
/// </remarks>
internal static class DefaultValueConverter
{
    /// <summary>The outcome: the JSON value, or the reason it could not be converted.</summary>
    public readonly record struct Result(bool HasValue, JsonNode? Value, string? Error);

    /// <summary>
    /// Converts the <c>[DefaultValue]</c> attribute <paramref name="attribute"/>. <paramref name="schemaType"/>
    /// is the JSON type of the schema the default goes on: an enum default is the member's name when
    /// the enum is written as strings (its name on the wire under <paramref name="enumNaming"/>), its
    /// number otherwise.
    /// </summary>
    public static Result FromAttribute(
        CustomAttributeData attribute, JsonSchemaType? schemaType, EnumWireNaming? enumNaming = null)
    {
        enumNaming ??= EnumWireNaming.MemberName;
        var args = attribute.ConstructorArguments;
        if (args.Count == 2 && args[0].Value is Type type && args[1].Value is string text)
            return FromText(type, text, schemaType, enumNaming);

        if (args.Count == 1 && args[0].Value is { } literal)
        {
            if (args[0].ArgumentType.IsEnum)
                return FromEnumValue(args[0].ArgumentType, literal, schemaType, enumNaming);
            return new Result(true, FromLiteral(literal), null);
        }

        return new Result(false, null, null);
    }

    private static bool WritesStrings(JsonSchemaType? schemaType) =>
        schemaType.HasValue && (schemaType.Value & JsonSchemaType.String) != 0;

    /// <summary>
    /// A C# default of an enum parameter (its raw underlying value), in the form of the enum's schema:
    /// the member's name for a string enum, else its number.
    /// </summary>
    public static Result FromEnumDefault(Type enumType, object raw, JsonSchemaType? schemaType) =>
        FromEnumValue(enumType, raw, schemaType, EnumWireNaming.MemberName);

    /// <summary>An enum member's raw value: its name for a string enum schema, else its number.</summary>
    private static Result FromEnumValue(Type enumType, object raw, JsonSchemaType? schemaType, EnumWireNaming enumNaming)
    {
        if (!WritesStrings(schemaType))
            return new Result(true, SchemaGenerator.IntegralValue(raw), null);

        var field = enumType.GetFields(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(f => Equals(f.GetRawConstantValue(), raw));
        return field != null
            ? new Result(true, JsonValue.Create(SchemaGenerator.EnumWireName(field, enumNaming)), null)
            : new Result(false, null, $"{raw} is not a named member of {enumType.Name}");
    }

    /// <summary>The value as System.Text.Json writes it (a JSON string for dates and times).</summary>
    private static JsonNode Wire<T>(T value) =>
        JsonValue.Create(JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(value))!);

    /// <summary>
    /// The JSON value of a literal: numbers stay numbers, <c>bool</c> a boolean, everything else
    /// its string form.
    /// </summary>
    public static JsonNode? FromLiteral(object literal) => literal switch
    {
        bool b      => JsonValue.Create(b),
        int i       => JsonValue.Create(i),
        long l      => JsonValue.Create(l),
        float f     => JsonValue.Create(f),
        double d    => JsonValue.Create(d),
        decimal dec => JsonValue.Create(dec),
        uint ui     => JsonValue.Create(ui),
        short s16   => JsonValue.Create(s16),
        ushort u16  => JsonValue.Create(u16),
        ulong u64   => JsonValue.Create((decimal)u64), // Microsoft.OpenApi writes no ulong value
        sbyte sb    => JsonValue.Create(sb),
        byte b8     => JsonValue.Create(b8),
        string s    => JsonValue.Create(s),
        _           => JsonValue.Create(Convert.ToString(literal, CultureInfo.InvariantCulture)),
    };

    /// <summary>Types whose text the converter reads; others are reported, never guessed.</summary>
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "System.Decimal", "System.Double", "System.Single", "System.Int32", "System.Int64", "System.Int16",
        "System.Byte", "System.SByte", "System.UInt16", "System.UInt32", "System.UInt64", "System.Boolean",
        "System.String", "System.Guid", "System.DateTime", "System.DateTimeOffset", "System.DateOnly",
        "System.TimeOnly", "System.TimeSpan", "System.Char",
    };

    private static Result FromText(Type type, string text, JsonSchemaType? schemaType, EnumWireNaming enumNaming)
    {
        if (type.IsEnum)
        {
            var field = type.GetField(text, BindingFlags.Public | BindingFlags.Static);
            return field == null
                ? new Result(false, null, $"\"{text}\" is not a member of {type.Name}")
                : FromEnumValue(type, field.GetRawConstantValue()!, schemaType, enumNaming);
        }

        var culture = CultureInfo.InvariantCulture;
        var name = type.FullName ?? type.Name;

        JsonNode? value = name switch
        {
            "System.Decimal" => decimal.TryParse(text, NumberStyles.Number, culture, out var dec) ? JsonValue.Create(dec) : null,
            "System.Double"  => double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var d) && double.IsFinite(d) ? JsonValue.Create(d) : null,
            "System.Single"  => float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var f) && float.IsFinite(f) ? JsonValue.Create(f) : null,
            "System.Int32"   => int.TryParse(text, NumberStyles.Integer, culture, out var i) ? JsonValue.Create(i) : null,
            "System.Int64"   => long.TryParse(text, NumberStyles.Integer, culture, out var l) ? JsonValue.Create(l) : null,
            "System.Int16"   => short.TryParse(text, NumberStyles.Integer, culture, out var s) ? JsonValue.Create(s) : null,
            "System.Byte"    => byte.TryParse(text, NumberStyles.Integer, culture, out var b8) ? JsonValue.Create(b8) : null,
            "System.SByte"   => sbyte.TryParse(text, NumberStyles.Integer, culture, out var sb) ? JsonValue.Create(sb) : null,
            "System.UInt16"  => ushort.TryParse(text, NumberStyles.Integer, culture, out var u16) ? JsonValue.Create(u16) : null,
            "System.UInt32"  => uint.TryParse(text, NumberStyles.Integer, culture, out var u32) ? JsonValue.Create(u32) : null,
            "System.UInt64"  => ulong.TryParse(text, NumberStyles.Integer, culture, out var u64) ? JsonValue.Create((decimal)u64) : null,
            "System.Boolean" => bool.TryParse(text, out var flag) ? JsonValue.Create(flag) : null,
            "System.String"  => JsonValue.Create(text),
            "System.Guid"    => Guid.TryParse(text, out var guid) ? JsonValue.Create(guid.ToString("D")) : null,
            "System.DateTime" => DateTime.TryParse(text, culture, DateTimeStyles.RoundtripKind, out var dt) ? Wire(dt) : null,
            "System.DateTimeOffset" => DateTimeOffset.TryParse(text, culture, DateTimeStyles.None, out var dto) ? Wire(dto) : null,
            "System.DateOnly" => DateOnly.TryParse(text, culture, out var date) ? Wire(date) : null,
            "System.TimeOnly" => TimeOnly.TryParse(text, culture, out var time) ? Wire(time) : null,
            "System.TimeSpan" => TimeSpan.TryParse(text, culture, out var span) ? Wire(span) : null,
            "System.Char"     => text.Length == 1 ? JsonValue.Create(text) : null,
            _ => null,
        };

        if (value != null)
            return new Result(true, value, null);

        return Known.Contains(name)
            ? new Result(false, null, $"\"{text}\" is not a valid {type.Name}")
            : new Result(false, null, $"values of {type.FullName} are not converted from text by the extractor");
    }
}
