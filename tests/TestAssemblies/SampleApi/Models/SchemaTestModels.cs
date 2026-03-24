using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SampleApi.Models;

// --- Inheritance ---

/// <summary>
/// Base entity with common properties
/// </summary>
public class BaseEntity
{
    /// <summary>Entity unique identifier</summary>
    public Guid Id { get; set; }
    /// <summary>Creation timestamp</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Derived entity with additional properties
/// </summary>
public sealed class DerivedEntity : BaseEntity
{
    /// <summary>Entity name</summary>
    [Required]
    [StringLength(200)]
    public required string Name { get; set; }
    /// <summary>Optional description</summary>
    public string? Description { get; set; }
}

// --- Records ---

/// <summary>
/// Positional record (properties from constructor)
/// </summary>
public record AddressRecord(
    string Street,
    string City,
    string? State,
    string ZipCode,
    string Country = "US"
);

// --- Nested and self-referencing types ---

/// <summary>
/// Tree node with self-reference
/// </summary>
public sealed class TreeNode
{
    /// <summary>Node value</summary>
    public required string Value { get; set; }
    /// <summary>Child nodes (self-reference)</summary>
    public List<TreeNode>? Children { get; set; }
    /// <summary>Parent node (nullable self-reference)</summary>
    public TreeNode? Parent { get; set; }
}

// --- Complex collections ---

/// <summary>
/// Model with various collection types
/// </summary>
public sealed class CollectionModel
{
    /// <summary>Simple string array</summary>
    public string[] Tags { get; set; } = [];
    /// <summary>List of integers</summary>
    public List<int> Scores { get; set; } = [];
    /// <summary>Set of unique strings</summary>
    public HashSet<string> UniqueNames { get; set; } = [];
    /// <summary>Dictionary of string to int</summary>
    public Dictionary<string, int> Counts { get; set; } = new();
    /// <summary>Nested generic: list of lists</summary>
    public List<List<string>>? NestedList { get; set; }
    /// <summary>Dictionary with complex value</summary>
    public Dictionary<string, UserProfile>? ProfileMap { get; set; }
    /// <summary>Read-only list</summary>
    public IReadOnlyList<Guid> ReadOnlyIds { get; set; } = [];
}

// --- JSON serialization attributes ---

/// <summary>
/// Model with JSON serialization customizations
/// </summary>
public sealed class JsonCustomModel
{
    /// <summary>Renamed property via JsonPropertyName</summary>
    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Always ignored property</summary>
    [JsonIgnore]
    public string InternalSecret { get; set; } = string.Empty;

    /// <summary>Conditionally ignored (only when null)</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OptionalField { get; set; }

    /// <summary>JSON required property</summary>
    [JsonRequired]
    public int ImportantValue { get; set; }

    /// <summary>Property with default value</summary>
    [DefaultValue(42)]
    public int DefaultNumber { get; set; } = 42;
}

// --- Validation attributes ---

/// <summary>
/// Model with various validation attributes
/// </summary>
public sealed class ValidationModel
{
    /// <summary>Required string with length constraints</summary>
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public required string Name { get; set; }

    /// <summary>Email address</summary>
    [EmailAddress]
    [StringLength(255)]
    public string? Email { get; set; }

    /// <summary>Age with range</summary>
    [Range(0, 150)]
    public int? Age { get; set; }

    /// <summary>Pattern-validated code</summary>
    [RegularExpression(@"^[A-Z]{2}\d{4}$")]
    public string? Code { get; set; }

    /// <summary>Max length on collection</summary>
    [MaxLength(10)]
    public List<string> Items { get; set; } = [];

    /// <summary>Min length string</summary>
    [MinLength(5)]
    public string? LongText { get; set; }
}

// --- Extended validation attributes ---

/// <summary>
/// Model with [Url], [Phone], [DefaultValue], [Obsolete], [Description], and MinLength on arrays.
/// </summary>
public sealed class ExtendedValidationModel
{
    /// <summary>Website address</summary>
    [Url]
    public string? Website { get; set; }

    /// <summary>Contact phone number</summary>
    [Phone]
    public string? PhoneNumber { get; set; }

    /// <summary>Numeric property with default value</summary>
    [DefaultValue(42)]
    public int Score { get; set; } = 42;

    /// <summary>Boolean property with default value</summary>
    [DefaultValue(true)]
    public bool IsActive { get; set; } = true;

    /// <summary>Description-only annotation (no XML doc overrides it)</summary>
    [System.ComponentModel.Description("The user's display alias")]
    public string? Alias { get; set; }

    /// <summary>Obsolete property</summary>
    [Obsolete("Use NewField instead")]
    public string? OldField { get; set; }

    /// <summary>Min items on array</summary>
    [MinLength(2)]
    public List<string> RequiredItems { get; set; } = [];

    /// <summary>Range with double bounds</summary>
    [Range(0.0, 1.0)]
    public double? Ratio { get; set; }
}

/// <summary>
/// Type decorated with [JsonUnmappedMemberHandling(Disallow)] so that
/// additionalProperties should be false in the generated schema.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StrictModel
{
    /// <summary>The only allowed property</summary>
    public string? Name { get; set; }
}

// --- All primitive types ---

/// <summary>
/// Model covering all C# primitive types for schema mapping
/// </summary>
public sealed class AllPrimitivesModel
{
    public string StringProp { get; set; } = string.Empty;
    public bool BoolProp { get; set; }
    public byte ByteProp { get; set; }
    public sbyte SByteProp { get; set; }
    public short ShortProp { get; set; }
    public ushort UShortProp { get; set; }
    public int IntProp { get; set; }
    public uint UIntProp { get; set; }
    public long LongProp { get; set; }
    public ulong ULongProp { get; set; }
    public float FloatProp { get; set; }
    public double DoubleProp { get; set; }
    public decimal DecimalProp { get; set; }
    public DateTime DateTimeProp { get; set; }
    public DateTimeOffset DateTimeOffsetProp { get; set; }
    public DateOnly DateOnlyProp { get; set; }
    public TimeOnly TimeOnlyProp { get; set; }
    public TimeSpan TimeSpanProp { get; set; }
    public Guid GuidProp { get; set; }
    public Uri? UriProp { get; set; }
    public char CharProp { get; set; }
}

// --- Nullable value types ---

/// <summary>
/// Model with nullable value type properties
/// </summary>
public sealed class NullableModel
{
    /// <summary>Nullable int</summary>
    public int? NullableInt { get; set; }
    /// <summary>Nullable bool</summary>
    public bool? NullableBool { get; set; }
    /// <summary>Nullable guid</summary>
    public Guid? NullableGuid { get; set; }
    /// <summary>Nullable enum</summary>
    public UserStatus? NullableEnum { get; set; }
    /// <summary>Nullable datetime</summary>
    public DateTimeOffset? NullableDate { get; set; }
    /// <summary>Non-nullable reference (NRT)</summary>
    public string NonNullableString { get; set; } = string.Empty;
    /// <summary>Nullable reference (NRT)</summary>
    public string? NullableString { get; set; }
}

// --- Enum with JsonStringEnumConverter ---

/// <summary>
/// Enum serialized as string
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Priority
{
    /// <summary>Low priority</summary>
    Low,
    /// <summary>Medium priority</summary>
    Medium,
    /// <summary>High priority</summary>
    High,
    /// <summary>Critical priority</summary>
    Critical
}

// --- Enum without converter (integer) ---
// UserStatus already exists in UserDto.cs — use it for integer enum tests

// --- Generic with multiple type args ---

/// <summary>
/// Paginated result with two type args
/// </summary>
public sealed class PaginatedResult<TItem, TMeta>
{
    /// <summary>Items in current page</summary>
    public List<TItem> Items { get; set; } = [];
    /// <summary>Pagination metadata</summary>
    public required TMeta Metadata { get; set; }
    /// <summary>Total item count</summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Pagination metadata
/// </summary>
public sealed class PaginationMeta
{
    /// <summary>Current page</summary>
    public int Page { get; set; }
    /// <summary>Page size</summary>
    public int PageSize { get; set; }
    /// <summary>Has next page</summary>
    public bool HasNext { get; set; }
}
