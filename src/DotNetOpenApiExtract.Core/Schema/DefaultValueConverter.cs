using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Turns a <c>[DefaultValue]</c> declaration (or a C# parameter default) into the JSON value of
/// <c>default</c>, one contract for DTO properties and action parameters.
/// </summary>
/// <remarks>
/// <c>[DefaultValue(Type, string)]</c> is converted to the type in the invariant culture: numbers
/// become JSON numbers, <c>bool</c> a boolean, <c>Guid</c> and other text-represented types a string.
/// A literal (<c>[DefaultValue(5)]</c>, <c>int page = 1</c>) keeps its JSON type.
/// </remarks>
internal static class DefaultValueConverter
{
    /// <summary>The outcome: the JSON value, or the reason it could not be converted.</summary>
    public readonly record struct Result(bool HasValue, JsonNode? Value, string? Error);

    /// <summary>Converts the <c>[DefaultValue]</c> attribute <paramref name="attribute"/>.</summary>
    public static Result FromAttribute(CustomAttributeData attribute)
    {
        var args = attribute.ConstructorArguments;
        if (args.Count == 2 && args[0].Value is Type type && args[1].Value is string text)
            return FromText(type, text);

        if (args.Count == 1)
            return args[0].Value is { } literal ? new Result(true, FromLiteral(literal), null) : new Result(false, null, null);

        return new Result(false, null, null);
    }

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

    private static Result FromText(Type type, string text)
    {
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
            "System.DateTime" => DateTime.TryParse(text, culture, DateTimeStyles.RoundtripKind, out _) ? JsonValue.Create(text) : null,
            "System.DateTimeOffset" => DateTimeOffset.TryParse(text, culture, DateTimeStyles.None, out _) ? JsonValue.Create(text) : null,
            "System.TimeSpan" => TimeSpan.TryParse(text, culture, out _) ? JsonValue.Create(text) : null,
            _ when type.IsEnum => type.GetField(text, BindingFlags.Public | BindingFlags.Static) != null ? JsonValue.Create(text) : null,
            _ => JsonValue.Create(text),
        };

        return value != null
            ? new Result(true, value, null)
            : new Result(false, null, $"\"{text}\" is not a valid {type.Name}");
    }
}
