using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace ModernApi.Models.Keywords;

/// <summary>A tint without a converter of its own.</summary>
public enum Tint
{
    /// <summary>Renamed for System.Text.Json.</summary>
    [JsonStringEnumMemberName("ruby")] Red,

    /// <summary>Renamed for Newtonsoft.Json.</summary>
    [EnumMember(Value = "leaf")] Green,

    /// <summary>Renamed for both serializers.</summary>
    [JsonStringEnumMemberName("scarlet"), EnumMember(Value = "crimson")] Crimson,

    /// <summary>Not renamed.</summary>
    Plain,
}

/// <summary>A tint written as strings by System.Text.Json's converter on the type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StjTint
{
    /// <summary>Renamed for System.Text.Json.</summary>
    [JsonStringEnumMemberName("ruby")] Red,

    /// <summary>Renamed for Newtonsoft.Json.</summary>
    [EnumMember(Value = "leaf")] Green,

    /// <summary>Renamed for both serializers.</summary>
    [JsonStringEnumMemberName("scarlet"), EnumMember(Value = "crimson")] Crimson,

    /// <summary>Not renamed.</summary>
    Plain,
}

/// <summary>A tint written as strings by the generic converter on the type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GenericStjTint>))]
public enum GenericStjTint
{
    /// <summary>Renamed for System.Text.Json.</summary>
    [JsonStringEnumMemberName("ruby")] Red,

    /// <summary>Renamed for Newtonsoft.Json.</summary>
    [EnumMember(Value = "leaf")] Green,

    /// <summary>Not renamed.</summary>
    Plain,
}

/// <summary>
/// Enums with a converter on the type, read under a global converter: System.Text.Json takes the
/// property's converter, then the options' converters, then the type's.
/// </summary>
public class GlobalOverTypeConverterModel
{
    /// <summary>Converter on the type only.</summary>
    [DefaultValue(StjTint.Green)]
    public StjTint TypeConverter { get; set; } = StjTint.Green;

    /// <summary>Generic converter on the type only.</summary>
    public GenericStjTint GenericTypeConverter { get; set; } = GenericStjTint.Plain;

    /// <summary>Converter on the property, over the global one.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StjTint PropertyConverter { get; set; } = StjTint.Crimson;
}

/// <summary>Enum members under every converter the extractor knows.</summary>
public class EnumWireNamesModel
{
    /// <summary>Converter on the type.</summary>
    public StjTint TypeConverter { get; set; }

    /// <summary>Generic converter on the type.</summary>
    public GenericStjTint GenericTypeConverter { get; set; }

    /// <summary>Nullable, converter on the type.</summary>
    public StjTint? NullableTypeConverter { get; set; }

    /// <summary>System.Text.Json's converter on the property.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Tint PropertyStj { get; set; }

    /// <summary>Newtonsoft.Json's converter on the property.</summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public Tint PropertyNewtonsoft { get; set; }

    /// <summary>The property's converter over the type's.</summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public StjTint PropertyOverType { get; set; }

    /// <summary>Nullable, System.Text.Json's converter on the property.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Tint? NullablePropertyStj { get; set; }

    /// <summary>Nullable, Newtonsoft.Json's converter on the property.</summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public Tint? NullablePropertyNewtonsoft { get; set; }

    /// <summary>Nullable, converter on the property, with a default and allowed values.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter)), DefaultValue(Tint.Crimson), AllowedValues(Tint.Red, Tint.Plain)]
    public Tint? NullableConstrained { get; set; } = Tint.Crimson;

    /// <summary>No converter: numbers.</summary>
    public Tint Numeric { get; set; }

    /// <summary>A default of a renamed member.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter)), DefaultValue(Tint.Red)]
    public Tint DefaultedStj { get; set; } = Tint.Red;

    /// <summary>A default of a renamed member, by name.</summary>
    [DefaultValue(typeof(StjTint), "Crimson")]
    public StjTint DefaultedByName { get; set; } = StjTint.Crimson;

    /// <summary>Allowed renamed members.</summary>
    [AllowedValues(StjTint.Red, StjTint.Crimson), DeniedValues(StjTint.Green)]
    public StjTint AllowedStj { get; set; } = StjTint.Red;
}
