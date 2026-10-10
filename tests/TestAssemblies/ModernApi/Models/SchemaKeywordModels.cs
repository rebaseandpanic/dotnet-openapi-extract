using System.ComponentModel.DataAnnotations;

namespace ModernApi.Models.Keywords;

/// <summary>Base64 data in every declared form.</summary>
public class Base64Payload
{
    /// <summary>Raw bytes.</summary>
    public required byte[] Data { get; set; }

    /// <summary>Optional raw bytes.</summary>
    public byte[]? OptionalData { get; set; }

    /// <summary>A base64 string.</summary>
    [Base64String]
    public required string Token { get; set; }

    /// <summary>An optional base64 string.</summary>
    [Base64String]
    public string? OptionalToken { get; set; }

    /// <summary>A plain string, for contrast.</summary>
    public required string Plain { get; set; }
}

/// <summary>Extension data as <c>IDictionary&lt;string, object&gt;</c>.</summary>
public class ExtensionObjectBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, object>? Extra { get; set; }
}

/// <summary>Extension data as <c>IDictionary&lt;string, JsonElement&gt;</c>.</summary>
public class ExtensionElementBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>Extension data as a concrete dictionary.</summary>
public class ExtensionDictionaryBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, object>? Extra { get; set; }
}

/// <summary>Extension data as <c>JsonObject</c>.</summary>
public class ExtensionJsonObjectBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public System.Text.Json.Nodes.JsonObject? Extra { get; set; }
}

/// <summary>Inherits its extension data.</summary>
public class ExtensionDerivedBag : ExtensionDictionaryBag
{
    /// <summary>Declared level.</summary>
    public int Level { get; set; }
}

/// <summary>Extension data that is ignored, so the object is not open.</summary>
public class ExtensionIgnoredBag
{
    /// <summary>Declared name.</summary>
    public required string Name { get; set; }

    /// <summary>Ignored member.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, object>? Extra { get; set; }
}

/// <summary>A generic type with extension data.</summary>
public class ExtensionEnvelope<T>
{
    /// <summary>The payload.</summary>
    public required T Payload { get; set; }

    /// <summary>Every other member.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}

/// <summary>Numeric ranges in every declared form.</summary>
public class RangeModel
{
    /// <summary>Inclusive integer range.</summary>
    [Range(1, 10)]
    public int Inclusive { get; set; }

    /// <summary>Integer range with an exclusive minimum.</summary>
    [Range(1, 10, MinimumIsExclusive = true)]
    public int ExclusiveMin { get; set; }

    /// <summary>Double range with an exclusive maximum.</summary>
    [Range(0.5, 9.5, MaximumIsExclusive = true)]
    public double ExclusiveMax { get; set; }

    /// <summary>Decimal range, both sides exclusive.</summary>
    [Range(typeof(decimal), "0.01", "999.99", MinimumIsExclusive = true, MaximumIsExclusive = true)]
    public decimal ExclusiveBoth { get; set; }

    /// <summary>Decimal range from strings.</summary>
    [Range(typeof(decimal), "0.01", "999.99")]
    public decimal Price { get; set; }

    /// <summary>Decimal range with 28 significant digits.</summary>
    [Range(typeof(decimal), "0.1234567890123456789012345678", "1")]
    public decimal Precise { get; set; }

    /// <summary>Nullable integer range.</summary>
    [Range(-5, 5)]
    public int? Optional { get; set; }

    /// <summary>A range over dates, which are not JSON numbers.</summary>
    [Range(typeof(DateTime), "2020-01-01", "2030-01-01")]
    public DateTime When { get; set; }

    /// <summary>A range with an infinite minimum.</summary>
    [Range(double.NegativeInfinity, 5.0)]
    public double Unbounded { get; set; }

    /// <summary>A float range from strings.</summary>
    [Range(typeof(float), "0.1", "2.5")]
    public float Ratio { get; set; }

    /// <summary>A Half range from strings.</summary>
    [Range(typeof(Half), "0.5", "10")]
    public Half Small { get; set; }

    /// <summary>A range with a NaN minimum, which RangeAttribute accepts.</summary>
    [Range(typeof(double), "NaN", "5")]
    public double NotANumberMin { get; set; }
}

/// <summary>A dictionary whose generic arguments are declared value first.</summary>
public class SwappedMap<TValue, TKey> : Dictionary<TKey, TValue>
    where TKey : notnull;

/// <summary>A key type written by a converter of its own.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(RegionCodeConverter))]
public readonly record struct RegionCode(string Value);

/// <summary>Converter for <see cref="RegionCode"/>, unknown to the extractor.</summary>
public sealed class RegionCodeConverter : System.Text.Json.Serialization.JsonConverter<RegionCode>
{
    /// <inheritdoc />
    public override RegionCode Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        new(reader.GetString()!);

    /// <inheritdoc />
    public override void Write(System.Text.Json.Utf8JsonWriter writer, RegionCode value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);

    /// <inheritdoc />
    public override RegionCode ReadAsPropertyName(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) =>
        new(reader.GetString()!);

    /// <inheritdoc />
    public override void WriteAsPropertyName(System.Text.Json.Utf8JsonWriter writer, RegionCode value, System.Text.Json.JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value);
}

/// <summary>Dictionaries keyed by every kind of key.</summary>
public class DictionaryModel
{
    /// <summary>Keyed by GUID.</summary>
    public Dictionary<Guid, int> ByGuid { get; set; } = [];

    /// <summary>Keyed by signed integer.</summary>
    public IDictionary<int, string> ByInt { get; set; } = new Dictionary<int, string>();

    /// <summary>Keyed by long.</summary>
    public IReadOnlyDictionary<long, string> ByLong { get; set; } = new Dictionary<long, string>();

    /// <summary>Keyed by unsigned integer.</summary>
    public Dictionary<uint, int> ByUint { get; set; } = [];

    /// <summary>Keyed by string.</summary>
    public Dictionary<string, int> ByName { get; set; } = [];

    /// <summary>Keyed by enum.</summary>
    public Dictionary<DayOfWeek, int> ByDay { get; set; } = [];

    /// <summary>Keyed by integer, declared value first.</summary>
    public SwappedMap<string, int> Swapped { get; set; } = [];

    /// <summary>Keyed by a type with its own converter.</summary>
    public Dictionary<RegionCode, int> ByRegion { get; set; } = [];

    /// <summary>A dictionary with a size range.</summary>
    [MinLength(1)]
    [MaxLength(5)]
    public Dictionary<string, int> Limited { get; set; } = [];
}

/// <summary>Shipping speed.</summary>
public enum ShippingSpeed
{
    /// <summary>Standard.</summary>
    Standard = 0,

    /// <summary>Express.</summary>
    Express = 1,

    /// <summary>Overnight.</summary>
    Overnight = 2,
}

/// <summary>A nested object for a reference with allowed values.</summary>
public class AllowedTarget
{
    /// <summary>Label.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>Allowed and denied values.</summary>
public class AllowedValuesModel
{
    /// <summary>Allowed strings, with a length limit.</summary>
    [AllowedValues("red", "green")]
    [MaxLength(10)]
    public string Color { get; set; } = "red";

    /// <summary>Allowed integers.</summary>
    [AllowedValues(1, 2)]
    public int Level { get; set; }

    /// <summary>One allowed string.</summary>
    [AllowedValues("only")]
    public string Single { get; set; } = "only";

    /// <summary>One allowed integer.</summary>
    [AllowedValues(7)]
    public int Lucky { get; set; }

    /// <summary>Denied strings.</summary>
    [DeniedValues("admin", "root")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Allowed members of an enum type with its own enum.</summary>
    [AllowedValues(ShippingSpeed.Standard, ShippingSpeed.Express)]
    public ShippingSpeed Speed { get; set; }

    /// <summary>A string where an integer is expected.</summary>
    [AllowedValues("one", "two")]
    public int Mismatched { get; set; }

    /// <summary>A reference with an allowed value, which goes into the reference's allOf wrapper.</summary>
    [AllowedValues("x")]
    public AllowedTarget Target { get; set; } = new();
}

/// <summary>An enum over long.</summary>
public enum WideCode : long
{
    /// <summary>Smallest.</summary>
    Smallest = long.MinValue,

    /// <summary>Largest.</summary>
    Largest = long.MaxValue,
}

/// <summary>An enum over ulong.</summary>
public enum HugeCode : ulong
{
    /// <summary>Zero.</summary>
    Zero = 0,

    /// <summary>Largest.</summary>
    Largest = ulong.MaxValue,
}

/// <summary>Unconstrained values and enums over 64-bit integers.</summary>
public class LooseValuesModel
{
    /// <summary>Any JSON value.</summary>
    public object Anything { get; set; } = new();

    /// <summary>Any JSON value, dynamically typed.</summary>
    public dynamic Dynamic { get; set; } = new object();

    /// <summary>An enum over long.</summary>
    public WideCode Wide { get; set; }

    /// <summary>An enum over ulong.</summary>
    public HugeCode Huge { get; set; }

    /// <summary>A long, for comparison.</summary>
    public long PlainLong { get; set; }

    /// <summary>A ulong, for comparison.</summary>
    public ulong PlainUlong { get; set; }
}

/// <summary>Defaults declared with a type and a string.</summary>
public class DefaultValuesModel
{
    /// <summary>A decimal default.</summary>
    [System.ComponentModel.DefaultValue(typeof(decimal), "1.5")]
    public decimal Rate { get; set; } = 1.5m;

    /// <summary>An integer default.</summary>
    [System.ComponentModel.DefaultValue(typeof(int), "42")]
    public int Count { get; set; } = 42;

    /// <summary>A boolean default.</summary>
    [System.ComponentModel.DefaultValue(typeof(bool), "true")]
    public bool Enabled { get; set; } = true;

    /// <summary>A string default.</summary>
    [System.ComponentModel.DefaultValue(typeof(string), "hello")]
    public string Greeting { get; set; } = "hello";

    /// <summary>A GUID default.</summary>
    [System.ComponentModel.DefaultValue(typeof(Guid), "0f8fad5b-d9cb-469f-a165-70867728950e")]
    public Guid Id { get; set; }

    /// <summary>A default that does not convert.</summary>
    [System.ComponentModel.DefaultValue(typeof(int), "many")]
    public int Broken { get; set; }

    /// <summary>A literal default.</summary>
    [System.ComponentModel.DefaultValue(5)]
    public int Literal { get; set; } = 5;

    /// <summary>An enum default by name on a numeric enum.</summary>
    [System.ComponentModel.DefaultValue(typeof(ShippingSpeed), "Express")]
    public ShippingSpeed Speed { get; set; } = ShippingSpeed.Express;

    /// <summary>An enum literal default on a numeric enum.</summary>
    [System.ComponentModel.DefaultValue(ShippingSpeed.Overnight)]
    public ShippingSpeed LiteralSpeed { get; set; } = ShippingSpeed.Overnight;

    /// <summary>An enum default on an enum written as strings.</summary>
    [System.ComponentModel.DefaultValue(typeof(ShippingSpeed), "Express")]
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ShippingSpeed NamedSpeed { get; set; } = ShippingSpeed.Express;

    /// <summary>A date default.</summary>
    [System.ComponentModel.DefaultValue(typeof(DateTime), "01/02/2024")]
    public DateTime Since { get; set; }

    /// <summary>A time span default.</summary>
    [System.ComponentModel.DefaultValue(typeof(TimeSpan), "1:02:03")]
    public TimeSpan Timeout { get; set; }

    /// <summary>A default of a type the extractor does not convert.</summary>
    [System.ComponentModel.DefaultValue(typeof(Uri), "https://example.com")]
    public Uri? Home { get; set; }
}

/// <summary>Defaults of every integer width other than int and long, as literals and as text.</summary>
public class NarrowIntegerDefaultsModel
{
    /// <summary>A byte literal default.</summary>
    [System.ComponentModel.DefaultValue((byte)200)]
    public byte ByteLiteral { get; set; } = 200;

    /// <summary>An sbyte literal default.</summary>
    [System.ComponentModel.DefaultValue((sbyte)-5)]
    public sbyte SByteLiteral { get; set; } = -5;

    /// <summary>A short literal default.</summary>
    [System.ComponentModel.DefaultValue((short)-300)]
    public short ShortLiteral { get; set; } = -300;

    /// <summary>A ushort literal default.</summary>
    [System.ComponentModel.DefaultValue((ushort)60000)]
    public ushort UShortLiteral { get; set; } = 60000;

    /// <summary>A uint literal default above int.MaxValue.</summary>
    [System.ComponentModel.DefaultValue(4000000000u)]
    public uint UIntLiteral { get; set; } = 4000000000u;

    /// <summary>A ulong literal default above long.MaxValue.</summary>
    [System.ComponentModel.DefaultValue(18446744073709551615UL)]
    public ulong ULongLiteral { get; set; } = 18446744073709551615UL;

    /// <summary>A byte default as text.</summary>
    [System.ComponentModel.DefaultValue(typeof(byte), "200")]
    public byte ByteText { get; set; } = 200;

    /// <summary>An sbyte default as text.</summary>
    [System.ComponentModel.DefaultValue(typeof(sbyte), "-5")]
    public sbyte SByteText { get; set; } = -5;

    /// <summary>A short default as text.</summary>
    [System.ComponentModel.DefaultValue(typeof(short), "-300")]
    public short ShortText { get; set; } = -300;

    /// <summary>A ushort default as text.</summary>
    [System.ComponentModel.DefaultValue(typeof(ushort), "60000")]
    public ushort UShortText { get; set; } = 60000;

    /// <summary>A uint default as text.</summary>
    [System.ComponentModel.DefaultValue(typeof(uint), "4000000000")]
    public uint UIntText { get; set; } = 4000000000u;

    /// <summary>A ulong default as text.</summary>
    [System.ComponentModel.DefaultValue(typeof(ulong), "18446744073709551615")]
    public ulong ULongText { get; set; } = 18446744073709551615UL;
}

/// <summary>Time spans, which System.Text.Json writes as [-][d.]hh:mm:ss[.fffffff].</summary>
public class TimeSpanModel
{
    /// <summary>A time span.</summary>
    public TimeSpan Plain { get; set; }

    /// <summary>A nullable time span.</summary>
    public TimeSpan? Optional { get; set; }

    /// <summary>A time span annotated as a duration.</summary>
    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Duration)]
    public TimeSpan Annotated { get; set; }

    /// <summary>A string annotated as a duration keeps the format.</summary>
    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Duration)]
    public string IsoText { get; set; } = "PT5S";
}

/// <summary>Allowed and denied values on members of an enum over ulong.</summary>
public class HugeCodeValuesModel
{
    /// <summary>Allowed: the largest member.</summary>
    [AllowedValues(HugeCode.Largest)]
    public HugeCode AllowedHuge { get; set; } = HugeCode.Largest;

    /// <summary>Denied: the largest member.</summary>
    [DeniedValues(HugeCode.Largest)]
    public HugeCode DeniedHuge { get; set; }
}

/// <summary>A delivery mode written as strings.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum DeliveryMode
{
    /// <summary>Pickup.</summary>
    Pickup = 0,

    /// <summary>Courier.</summary>
    Courier = 1,
}

/// <summary>Enum defaults in both forms.</summary>
public class EnumDefaultsModel
{
    /// <summary>A literal default of a string enum.</summary>
    [System.ComponentModel.DefaultValue(DeliveryMode.Courier)]
    public DeliveryMode Mode { get; set; } = DeliveryMode.Courier;
}
