using System.Text.Json.Serialization;

namespace ModernApi.Models.Keywords;

/// <summary>A nested object with an example of its own.</summary>
/// <example>{"label": "from the type"}</example>
public class ExampleTarget
{
    /// <summary>Label.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>A base type whose property is inherited.</summary>
public class ExampleBase
{
    /// <summary>Inherited.</summary>
    /// <example>17</example>
    public int Inherited { get; set; }
}

/// <summary>Examples of every JSON type of schema.</summary>
/// <example>{"count": 3, "code": "abc"}</example>
public class ExampleModel : ExampleBase
{
    /// <summary>An integer.</summary>
    /// <example>42</example>
    public int Count { get; set; }

    /// <summary>A large integer, written without loss.</summary>
    /// <example>9007199254740993</example>
    public long Big { get; set; }

    /// <summary>A number.</summary>
    /// <example>1.5</example>
    public double Ratio { get; set; }

    /// <summary>A boolean.</summary>
    /// <example>true</example>
    public bool Enabled { get; set; }

    /// <summary>A string that looks like a number.</summary>
    /// <example>123</example>
    public string Code { get; set; } = string.Empty;

    /// <summary>A date, written as a string.</summary>
    /// <example>2024-01-02</example>
    public DateOnly Day { get; set; }

    /// <summary>A dictionary.</summary>
    /// <example>{"a": 1}</example>
    public Dictionary<string, int> Map { get; set; } = [];

    /// <summary>A list.</summary>
    /// <example>[1, 2]</example>
    public List<int> Items { get; set; } = [];

    /// <summary>A nullable string.</summary>
    /// <example>null</example>
    public string? Note { get; set; }

    /// <summary>A nullable number.</summary>
    /// <example>null</example>
    public int? MaybeCount { get; set; }

    /// <example>7</example>
    public int WithoutSummary { get; set; }

    /// <summary>A reference.</summary>
    /// <example>{"label": "from the property"}</example>
    public ExampleTarget Target { get; set; } = new();

    /// <summary>A number read from strings.</summary>
    /// <example>5</example>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Lenient { get; set; }

    /// <summary>A string whose example keeps its inner spaces.</summary>
    /// <example>two  spaces</example>
    public string Spaced { get; set; } = string.Empty;

    /// <summary>An object whose string value keeps its inner spaces.</summary>
    /// <example>{"text": "a  b"}</example>
    public Dictionary<string, string> SpacedObject { get; set; } = [];

    /// <summary>An example over several lines.</summary>
    /// <example>
    /// {
    ///   "a": 1
    /// }
    /// </example>
    public Dictionary<string, int> MultiLine { get; set; } = [];

    /// <summary>A string enum.</summary>
    /// <example>ruby</example>
    public StjTint Tint { get; set; }
}

/// <summary>A positional record.</summary>
/// <param name="Amount" example="250">The amount.</param>
/// <param name="Currency" example="EUR">The currency.</param>
public record ExampleRecord(int Amount, string Currency);

/// <summary>Examples that cannot be written.</summary>
public class ExampleFailuresModel
{
    /// <summary>Not a number.</summary>
    /// <example>many</example>
    public int NotANumber { get; set; }

    /// <summary>A fraction for an integer.</summary>
    /// <example>1.5</example>
    public int Fraction { get; set; }

    /// <summary>Null for a non-nullable number.</summary>
    /// <example>null</example>
    public int NotNullable { get; set; }

    /// <summary>Null for a non-nullable string.</summary>
    /// <example>null</example>
    public string NotNullableText { get; set; } = string.Empty;

    /// <summary>An object for an array.</summary>
    /// <example>{"a": 1}</example>
    public List<int> WrongKind { get; set; } = [];

    /// <summary>Broken JSON.</summary>
    /// <example>[1,</example>
    public List<int> Broken { get; set; } = [];

    /// <summary>Two examples.</summary>
    /// <example>1</example>
    /// <example>2</example>
    public int Twice { get; set; }

    /// <summary>Two examples, the first not a number.</summary>
    /// <example>x</example>
    /// <example>2</example>
    public int TwiceBroken { get; set; }
}

/// <summary>Constraints and examples on numbers read from strings.</summary>
public class NumberPlacementModel
{
    /// <summary>Bounded, nullable, read from strings.</summary>
    /// <example>5</example>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    [System.ComponentModel.DataAnnotations.Range(1, 10)]
    public int? Bounded { get; set; }

    /// <summary>Items read from strings.</summary>
    /// <example>[1, 2]</example>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public List<int> Items { get; set; } = [];
}

/// <summary>A polymorphic base with an example of the union.</summary>
/// <example>{"kind": "circle", "radius": 2}</example>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExampleCircle), "circle")]
public abstract class ExampleShape
{
}

/// <summary>A circle.</summary>
public sealed class ExampleCircle : ExampleShape
{
    /// <summary>Radius.</summary>
    public int Radius { get; set; }
}

/// <summary>A polymorphic base whose examples cannot be written.</summary>
/// <example>[1,</example>
/// <example>{}</example>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExampleBrokenSquare), "square")]
public abstract class ExampleBrokenShape
{
}

/// <summary>A square.</summary>
public sealed class ExampleBrokenSquare : ExampleBrokenShape
{
    /// <summary>Side.</summary>
    public int Side { get; set; }
}
