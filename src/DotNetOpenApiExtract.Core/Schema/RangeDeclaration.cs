using System.Globalization;
using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// A <c>[Range]</c> declaration read from metadata, as <c>RangeAttribute</c> of .NET 10 interprets it:
/// the <c>(int, int)</c>, <c>(double, double)</c> and <c>(Type, string, string)</c> constructors and
/// the <c>MinimumIsExclusive</c> / <c>MaximumIsExclusive</c> properties. String bounds are parsed in the
/// invariant culture into <c>decimal</c> (integer and decimal operands) or <c>double</c> (floating-point
/// operands), never through binary floating point for a decimal.
/// </summary>
internal sealed class RangeDeclaration
{
    /// <summary>What the declaration means for a schema.</summary>
    public enum Outcome
    {
        /// <summary>Numeric bounds; a bound may be non-finite (not expressible in JSON).</summary>
        Numeric,

        /// <summary>The operand type is not numeric (for example <c>DateTime</c>): no numeric constraint.</summary>
        NonNumericOperand,

        /// <summary><c>RangeAttribute</c> itself rejects the declaration when it validates.</summary>
        Invalid,
    }

    public required Outcome Kind { get; init; }

    /// <summary>The JSON number lexeme of the minimum, or <see langword="null"/> when it is not finite.</summary>
    public string? Minimum { get; init; }

    /// <summary>The JSON number lexeme of the maximum, or <see langword="null"/> when it is not finite.</summary>
    public string? Maximum { get; init; }

    /// <summary>The text of a bound that cannot be written to JSON (<c>NaN</c>, <c>Infinity</c>), per side.</summary>
    public string? NonFiniteMinimum { get; init; }

    /// <inheritdoc cref="NonFiniteMinimum"/>
    public string? NonFiniteMaximum { get; init; }

    public bool MinimumIsExclusive { get; init; }

    public bool MaximumIsExclusive { get; init; }

    /// <summary>For <see cref="Outcome.NonNumericOperand"/>, the operand type; for <see cref="Outcome.Invalid"/>, the reason.</summary>
    public string? Detail { get; init; }

    private static readonly HashSet<string> IntegerTypes = new(StringComparer.Ordinal)
    {
        "System.Byte", "System.SByte", "System.Int16", "System.UInt16",
        "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
    };

    /// <summary>Reads <paramref name="attribute"/>, or <see langword="null"/> for a constructor shape it does not know.</summary>
    public static RangeDeclaration? Read(CustomAttributeData attribute)
    {
        var args = attribute.ConstructorArguments;
        var minExclusive = AttributeHelper.GetNamedArgument<bool>(attribute, "MinimumIsExclusive");
        var maxExclusive = AttributeHelper.GetNamedArgument<bool>(attribute, "MaximumIsExclusive");

        if (args.Count == 2 && args[0].Value is int minInt && args[1].Value is int maxInt)
            return FromDecimals(minInt, maxInt, minExclusive, maxExclusive);

        if (args.Count == 2 && args[0].Value is double minDouble && args[1].Value is double maxDouble)
            return FromDoubles(minDouble, maxDouble, minExclusive, maxExclusive);

        if (args.Count == 3 && args[0].Value is Type operand && args[1].Value is string minText && args[2].Value is string maxText)
            return FromStrings(operand, minText, maxText, minExclusive, maxExclusive);

        return null;
    }

    private static RangeDeclaration FromStrings(Type operand, string minText, string maxText, bool minExclusive, bool maxExclusive)
    {
        var name = operand.FullName ?? operand.Name;
        if (IntegerTypes.Contains(name))
        {
            if (!TryParseInteger(name, minText, out var min) || !TryParseInteger(name, maxText, out var max))
                return Invalid($"a bound is not a valid {operand.Name}");
            return FromDecimals(min, max, minExclusive, maxExclusive);
        }

        if (name == "System.Decimal")
        {
            const NumberStyles style = NumberStyles.Number;
            if (!decimal.TryParse(minText, style, CultureInfo.InvariantCulture, out var min)
                || !decimal.TryParse(maxText, style, CultureInfo.InvariantCulture, out var max))
                return Invalid($"a bound is not a valid {operand.Name}");
            return FromDecimals(min, max, minExclusive, maxExclusive);
        }

        if (name is "System.Double" or "System.Single")
        {
            const NumberStyles style = NumberStyles.Float | NumberStyles.AllowThousands;
            double min, max;
            if (name == "System.Single")
            {
                if (!float.TryParse(minText, style, CultureInfo.InvariantCulture, out var minFloat)
                    || !float.TryParse(maxText, style, CultureInfo.InvariantCulture, out var maxFloat))
                    return Invalid($"a bound is not a valid {operand.Name}");
                (min, max) = (minFloat, maxFloat);
            }
            else if (!double.TryParse(minText, style, CultureInfo.InvariantCulture, out min)
                     || !double.TryParse(maxText, style, CultureInfo.InvariantCulture, out max))
            {
                return Invalid($"a bound is not a valid {operand.Name}");
            }

            return FromDoubles(min, max, minExclusive, maxExclusive);
        }

        return new RangeDeclaration { Kind = Outcome.NonNumericOperand, Detail = name };
    }

    private static bool TryParseInteger(string typeName, string text, out decimal value)
    {
        const NumberStyles style = NumberStyles.Integer;
        var culture = CultureInfo.InvariantCulture;
        bool ok;
        (ok, value) = typeName switch
        {
            "System.Byte"   => (byte.TryParse(text, style, culture, out var b), b),
            "System.SByte"  => (sbyte.TryParse(text, style, culture, out var sb), sb),
            "System.Int16"  => (short.TryParse(text, style, culture, out var s), s),
            "System.UInt16" => (ushort.TryParse(text, style, culture, out var us), us),
            "System.Int32"  => (int.TryParse(text, style, culture, out var i), i),
            "System.UInt32" => (uint.TryParse(text, style, culture, out var ui), ui),
            "System.Int64"  => (long.TryParse(text, style, culture, out var l), l),
            _               => (ulong.TryParse(text, style, culture, out var ul), (decimal)ul),
        };
        return ok;
    }

    private static RangeDeclaration FromDecimals(decimal min, decimal max, bool minExclusive, bool maxExclusive)
    {
        var comparison = min.CompareTo(max);
        if (CheckOrder(comparison, minExclusive, maxExclusive) is { } invalid)
            return invalid;

        return new RangeDeclaration
        {
            Kind               = Outcome.Numeric,
            Minimum            = min.ToString(CultureInfo.InvariantCulture),
            Maximum            = max.ToString(CultureInfo.InvariantCulture),
            MinimumIsExclusive = minExclusive,
            MaximumIsExclusive = maxExclusive,
        };
    }

    private static RangeDeclaration FromDoubles(double min, double max, bool minExclusive, bool maxExclusive)
    {
        // double.CompareTo orders NaN below every number, as RangeAttribute's comparison does.
        if (CheckOrder(min.CompareTo(max), minExclusive, maxExclusive) is { } invalid)
            return invalid;

        return new RangeDeclaration
        {
            Kind               = Outcome.Numeric,
            Minimum            = double.IsFinite(min) ? min.ToString("R", CultureInfo.InvariantCulture) : null,
            Maximum            = double.IsFinite(max) ? max.ToString("R", CultureInfo.InvariantCulture) : null,
            NonFiniteMinimum   = double.IsFinite(min) ? null : min.ToString(CultureInfo.InvariantCulture),
            NonFiniteMaximum   = double.IsFinite(max) ? null : max.ToString(CultureInfo.InvariantCulture),
            MinimumIsExclusive = minExclusive,
            MaximumIsExclusive = maxExclusive,
        };
    }

    /// <summary>The two checks RangeAttribute makes before it compares a value.</summary>
    private static RangeDeclaration? CheckOrder(int comparison, bool minExclusive, bool maxExclusive)
    {
        if (comparison > 0)
            return Invalid("the minimum is greater than the maximum");
        if (comparison == 0 && (minExclusive || maxExclusive))
            return Invalid("exclusive bounds are used with equal minimum and maximum");
        return null;
    }

    private static RangeDeclaration Invalid(string reason) => new() { Kind = Outcome.Invalid, Detail = reason };
}
