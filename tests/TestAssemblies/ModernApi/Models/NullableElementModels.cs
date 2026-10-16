using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModernApi.Models.Keywords;

namespace ModernApi.Models.NullableElements;

/// <summary>An element component, referenced from nullable and non-nullable positions.</summary>
public class ElementItem
{
    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// An element component of typed results only. Typed results are written with the HTTP JSON options and
/// MVC actions with the MVC ones, so a type in both would get a schema per serialization context.
/// </summary>
public class TypedElementItem
{
    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>A polymorphic base used as an element.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ElementCircle), "circle")]
public abstract class ElementShape
{
}

/// <summary>A circle.</summary>
public sealed class ElementCircle : ElementShape
{
    /// <summary>Radius.</summary>
    public int Radius { get; set; }
}

/// <summary>A dictionary whose type arguments are declared in the reverse order of its base.</summary>
/// <typeparam name="TValue">The value, the second argument of the base dictionary.</typeparam>
/// <typeparam name="TKey">The key, the first argument of the base dictionary.</typeparam>
public class Swapped<TValue, TKey> : Dictionary<TKey, TValue>
    where TKey : notnull
{
}

/// <summary>A generic page whose items are declared nullable on the type parameter.</summary>
/// <typeparam name="T">The item type.</typeparam>
public class Page<T>
{
    /// <summary>Items of the page.</summary>
    public List<T?> Items { get; set; } = [];
}

/// <summary>Collections, arrays and dictionaries with nullable reference elements and values.</summary>
public class NullableElementsModel
{
    // Collections.

    /// <summary>Nullable strings.</summary>
    public List<string?> Strings { get; set; } = [];

    /// <summary>An array of nullable items.</summary>
    public ElementItem?[] ItemArray { get; set; } = [];

    /// <summary>A sequence of nullable items.</summary>
    public IEnumerable<ElementItem?> ItemSequence { get; set; } = [];

    /// <summary>A set of nullable strings.</summary>
    public HashSet<string?> StringSet { get; set; } = [];

    /// <summary>A read-only list of nullable items.</summary>
    public IReadOnlyList<ElementItem?> ItemReadOnlyList { get; set; } = [];

    // Dictionaries.

    /// <summary>Nullable item values by name.</summary>
    public Dictionary<string, ElementItem?> ItemsByName { get; set; } = [];

    /// <summary>Nullable string values by identifier.</summary>
    public IReadOnlyDictionary<Guid, string?> StringsById { get; set; } = new Dictionary<Guid, string?>();

    /// <summary>Nullable item values of a dictionary that declares its value first.</summary>
    public Swapped<ElementItem?, string> SwappedItems { get; set; } = [];

    // Nested generics.

    /// <summary>Nullable lists of nullable items.</summary>
    public List<List<ElementItem?>?> NestedLists { get; set; } = [];

    /// <summary>Lists of nullable items by name.</summary>
    public Dictionary<string, List<ElementItem?>> ListsByName { get; set; } = [];

    /// <summary>An array of lists of nullable items.</summary>
    public List<ElementItem?>[] ListArray { get; set; } = [];

    // Elements whose schema already admits null or is a value type.

    /// <summary>Nullable objects: the element schema is <c>{}</c>.</summary>
    public List<object?> NullableObjects { get; set; } = [];

    /// <summary>Objects without an annotation: the element schema is <c>{}</c>.</summary>
    public List<object> Objects { get; set; } = [];

    /// <summary>Nullable integers: <c>Nullable&lt;int&gt;</c>.</summary>
    public List<int?> NullableInts { get; set; } = [];

    /// <summary>Strings without an annotation (NRT byte 1).</summary>
    public List<string> NotNullStrings { get; set; } = [];

    /// <summary>Items without an annotation (NRT byte 1).</summary>
    public List<ElementItem> NotNullItems { get; set; } = [];

    // Generic pages.

    /// <summary>A page of items: <c>T?</c> closed by a reference type.</summary>
    public Page<ElementItem> ItemPage { get; set; } = new();

    /// <summary>A page of integers: <c>T?</c> closed by a value type.</summary>
    public Page<int> IntPage { get; set; } = new();

    // Elements of other schema kinds.

    /// <summary>Nullable polymorphic shapes.</summary>
    public List<ElementShape?> Shapes { get; set; } = [];

    /// <summary>Nullable dictionaries with reference values.</summary>
    public List<Dictionary<string, ElementItem>?> Dictionaries { get; set; } = [];

    /// <summary>Nullable enum values written as strings.</summary>
    public List<StjTint?> Tints { get; set; } = [];

    /// <summary>Nullable enum values written as numbers.</summary>
    public List<Tint?> NumericTints { get; set; } = [];

    /// <summary>Nullable enum values of a dictionary, written as strings.</summary>
    public Dictionary<string, StjTint?> TintsByName { get; set; } = [];

    /// <summary>Nullable JSON objects.</summary>
    public List<JsonObject?> NullableJsonObjects { get; set; } = [];

    /// <summary>Nullable JSON arrays.</summary>
    public List<JsonArray?> NullableJsonArrays { get; set; } = [];
}

/// <summary>The same BCL JSON types as non-nullable elements, in another place.</summary>
public class JsonElementsModel
{
    /// <summary>JSON objects.</summary>
    public List<JsonObject> JsonObjects { get; set; } = [];

    /// <summary>JSON arrays.</summary>
    public List<JsonArray> JsonArrays { get; set; } = [];
}

/// <summary>One <see cref="ElementItem"/> in three places: a nullable element, an element and a described property.</summary>
public class ElementItemPlaces
{
    /// <summary>Nullable items.</summary>
    public List<ElementItem?> NullableItems { get; set; } = [];

    /// <summary>Items.</summary>
    public List<ElementItem> Items { get; set; } = [];

    /// <summary>The item of this place, described here.</summary>
    public ElementItem Item { get; set; } = new();
}

#nullable disable
/// <summary>Collections declared in a <c>#nullable disable</c> context (NRT byte 0).</summary>
public class ObliviousElementsModel
{
    /// <summary>Strings.</summary>
    public List<string> Strings { get; set; } = [];

    /// <summary>Items.</summary>
    public List<ElementItem> Items { get; set; } = [];

    /// <summary>Item values by name.</summary>
    public Dictionary<string, ElementItem> ItemsByName { get; set; } = [];

    /// <summary>An array of items.</summary>
    public ElementItem[] ItemArray { get; set; } = [];
}
#nullable restore
