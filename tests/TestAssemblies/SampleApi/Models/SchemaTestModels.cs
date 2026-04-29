using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

/// <summary>DTO marked obsolete at the class level.</summary>
[Obsolete("This DTO is deprecated, use a newer model instead")]
public sealed class ObsoleteDto
{
    /// <summary>The name field.</summary>
    public string Name { get; set; } = "";
}

/// <summary>Enum marked obsolete at type level.</summary>
[Obsolete("This enum is deprecated")]
public enum ObsoleteEnum
{
    /// <summary>Red value.</summary>
    Red,
    /// <summary>Green value.</summary>
    Green,
    /// <summary>Blue value.</summary>
    Blue,
}

/// <summary>Enum with no XML-doc summaries on its values — verifies that <c>x-enum-descriptions</c> is NOT emitted but <c>x-enum-varnames</c> IS emitted.</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
}

/// <summary>Enum with partial XML-doc summaries — only some values are documented.</summary>
public enum TrafficLight
{
    /// <summary>Stop — vehicles must halt.</summary>
    Red,
    Yellow,
    /// <summary>Go — vehicles may proceed.</summary>
    Green,
}

/// <summary>
/// Model exercising C# 11+ required modifier (RequiredMemberAttribute)
/// to ensure it maps to OpenAPI required[] alongside [Required].
/// </summary>
public class RequiredModifierModel
{
    /// <summary>Must be provided — via required modifier.</summary>
    public required string ViaModifier { get; set; }

    /// <summary>Must be provided — via required modifier, value type.</summary>
    public required int CountViaModifier { get; set; }

    /// <summary>Must be provided — via [Required] attribute.</summary>
    [Required]
    public string ViaAttribute { get; set; } = "";

    /// <summary>Must be provided — both modifier and attribute.</summary>
    [Required]
    public required string ViaBoth { get; set; }

    /// <summary>Optional — no required markers.</summary>
    public string? Optional { get; set; }

    /// <summary>Nullable reference + required modifier — NRT says optional, modifier says required.</summary>
    public required string? NullableRefViaModifier { get; set; }

    /// <summary>required + [JsonIgnore] — skipped entirely, consistent with [JsonIgnore] + [Required].</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public required string JsonIgnoredViaModifier { get; set; }
}

/// <summary>
/// Model used to verify that a property-level [Description] attribute wins over
/// the description injected by the JsonConverterRegistry converter hint.
/// Regression target for the BREAKING change: previously the converter hint
/// description won; now [Description] takes precedence.
/// </summary>
public class DescriptionOverridesConverterHintModel
{
    /// <summary>Unix timestamp — description must come from [Description], not the converter hint.</summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.UnixDateTimeConverter))]
    [Description("Custom description wins over converter hint")]
    public DateTime CreatedUnix { get; set; }
}

// =========================================================================
// Enum fixtures for x-enum-varnames, auto-description, and [Description] fallback tests
// =========================================================================

/// <summary>Order status enumeration.</summary>
public enum OrderStatus
{
    /// <summary>Order created but not submitted</summary>
    Draft,
    /// <summary>Awaiting payment</summary>
    Pending,
    /// <summary>Payment received, processing</summary>
    Processing,
    /// <summary>Order shipped</summary>
    Shipped,
}

/// <summary>Payment method type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PaymentMethod
{
    /// <summary>Credit or debit card</summary>
    Card,
    /// <summary>Bank transfer</summary>
    BankTransfer,
    /// <summary>Cash on delivery</summary>
    Cash,
}

/// <summary>Severity level using [Description] attributes instead of XML docs.</summary>
public enum SeverityLevel
{
    [Description("Informational message, no action required")]
    Info,
    [Description("Potential issue, review recommended")]
    Warning,
    [Description("Critical failure, immediate action required")]
    Critical,
}

/// <summary>Mixed documentation: some values use XML docs, others use [Description].</summary>
public enum MixedDocEnum
{
    /// <summary>Active and running</summary>
    Active,
    [Description("Temporarily paused")]
    Paused,
    Stopped,
}

/// <summary>Enum with no documentation on any value — varnames still emitted, descriptions not.</summary>
public enum UndocumentedEnum
{
    Alpha,
    Beta,
    Gamma,
}

// =========================================================================
// Positional record fixtures — verify default-target attributes on primary
// constructor parameters merge with property attributes.
// =========================================================================

/// <summary>
/// Positional record where validation and description attributes are placed on
/// primary-constructor parameters using the C# default attribute target
/// (no [param:] / [property:] prefix). The compiler emits these onto the parameter
/// in IL, not onto the synthesized property — the schema generator must merge
/// them in.
/// JSON attributes such as [JsonIgnore] cannot be used here because their
/// AttributeUsage forbids Parameter (CS0592) — they must be applied with the
/// explicit [property:] target, which the existing PropertyInfo path handles.
/// </summary>
/// <param name="Name">Customer full name</param>
/// <param name="Age">Customer age in years</param>
/// <param name="Code">Tracking code (two letters + four digits)</param>
/// <param name="Email">Optional email address</param>
/// <param name="Description">Free-form description for the customer record</param>
public record CreatePositionalCustomerRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Range(0, 150)] int Age,
    [RegularExpression(@"^[A-Z]{2}\d{4}$")] string Code,
    [EmailAddress] string? Email,
    [System.ComponentModel.Description("Internal description override")] string? Description
);

/// <summary>
/// Positional record mixing default-target attributes (param) with explicit
/// [property:] target attributes on the same and adjacent parameters. Both
/// must be reflected in the resulting schema.
/// </summary>
/// <param name="Title">Title of the resource</param>
/// <param name="Subtitle">Optional subtitle</param>
public record MixedTargetPositionalRecord(
    [Required, StringLength(50)] string Title,
    [property: System.ComponentModel.Description("Property-target description")] string? Subtitle
);

/// <summary>
/// Positional record with a default value on a parameter via [DefaultValue].
/// </summary>
/// <param name="Country">ISO country code</param>
/// <param name="Currency">Currency code, optional with default</param>
public record PositionalDefaultsRecord(
    [DefaultValue("US")] string Country = "US",
    string Currency = "USD"
);

/// <summary>
/// Base positional record used in inheritance fixture below.
/// </summary>
/// <param name="Id">Identifier on the base</param>
public record BasePositionalRecord([Required, StringLength(36)] string Id);

/// <summary>
/// Derived positional record — adds a new property with its own default-target
/// attribute. The base property's attributes must come from the base ctor; the
/// derived property's attributes must come from the derived ctor.
/// </summary>
/// <param name="Id">Identifier inherited from base</param>
/// <param name="Extra">Additional field on derived record</param>
public record DerivedPositionalRecord(
    string Id,
    [StringLength(20)] string Extra
) : BasePositionalRecord(Id);

/// <summary>
/// Class with a C# 12 primary constructor whose parameters mirror the explicitly
/// declared properties by name. Default-target attributes on the primary-ctor
/// parameters must propagate to those properties' schemas.
/// </summary>
public class PrimaryCtorClassModel([Required, StringLength(50)] string Name, [Range(1, 100)] int Quantity)
{
    /// <summary>Mirrored Name property</summary>
    public string Name { get; init; } = Name;

    /// <summary>Mirrored Quantity property</summary>
    public int Quantity { get; init; } = Quantity;
}

/// <summary>
/// Positional record with a secondary user-written constructor that uses the same
/// parameter name as the primary record constructor but carries no validation
/// attributes. The attribute merge must prefer the primary ctor's parameter
/// (which has [Required, StringLength]) — selecting the secondary ctor would
/// silently drop the validation attributes.
/// </summary>
/// <param name="Name">Primary name</param>
public record SecondaryCtorRecord([Required, StringLength(64)] string Name)
{
    // Secondary constructor with the same parameter name but no attrs.
    // Reflection's GetConstructors() ordering is unspecified, so the merge
    // logic must not return this constructor's empty-attr parameter.
    public SecondaryCtorRecord(string Name, int unused) : this(Name) { _ = unused; }
}
