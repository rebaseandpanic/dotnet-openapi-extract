using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Provides a hardcoded registry that maps well-known JSON converter type full names
/// to <see cref="ConverterSchemaHint"/> values describing how those converters affect
/// the OpenAPI schema of the types they are applied to.
/// </summary>
/// <remarks>
/// The registry is used in two places:
/// <list type="bullet">
///   <item>Attribute-level — when <c>[JsonConverter(typeof(X))]</c> decorates a property or type.</item>
///   <item>Global-level — when converter type full names are supplied via
///         <see cref="SchemaOptions.GlobalConverterTypeNames"/>.</item>
/// </list>
/// Only converters whose effect on serialization can be reliably inferred from their type
/// alone are included. Unknown converters leave schema generation unchanged; a warning is
/// emitted via <c>Console.Error</c> (deduplicated per <see cref="SchemaGenerator"/> instance).
/// </remarks>
public static class JsonConverterRegistry
{
    // -------------------------------------------------------------------------
    // Known converter FullNames (non-generic and generic-definition forms)
    // -------------------------------------------------------------------------

    // System.Text.Json
    private const string JsonStringEnumConverter =
        "System.Text.Json.Serialization.JsonStringEnumConverter";

    private const string JsonStringEnumConverterGeneric =
        "System.Text.Json.Serialization.JsonStringEnumConverter`1";

    // System.Text.Json.Serialization.JsonStringEnumMember (community package)
    private const string JsonStringEnumMemberConverter =
        "System.Text.Json.Serialization.JsonStringEnumMemberConverter";

    // Newtonsoft.Json
    private const string NewtonsoftStringEnumConverter =
        "Newtonsoft.Json.Converters.StringEnumConverter";

    private const string IsoDateTimeConverter =
        "Newtonsoft.Json.Converters.IsoDateTimeConverter";

    private const string JavaScriptDateTimeConverter =
        "Newtonsoft.Json.Converters.JavaScriptDateTimeConverter";

    private const string UnixDateTimeConverter =
        "Newtonsoft.Json.Converters.UnixDateTimeConverter";

    // -------------------------------------------------------------------------
    // Special target type marker
    // -------------------------------------------------------------------------

    /// <summary>
    /// Special marker meaning "applies to any enum type".
    /// Used in <see cref="ConverterSchemaHint.TargetTypeFullNames"/>.
    /// </summary>
    public const string AnyEnumTarget = "enum";

    // -------------------------------------------------------------------------
    // Registry lookup table
    // -------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, ConverterSchemaHint> Registry =
        new Dictionary<string, ConverterSchemaHint>(StringComparer.Ordinal)
        {
            // System.Text.Json — JsonStringEnumConverter (non-generic)
            [JsonStringEnumConverter] = new ConverterSchemaHint
            {
                SchemaType = JsonSchemaType.String,
                TargetTypeFullNames = [AnyEnumTarget],
            },

            // System.Text.Json — JsonStringEnumConverter<T> (open generic definition)
            [JsonStringEnumConverterGeneric] = new ConverterSchemaHint
            {
                SchemaType = JsonSchemaType.String,
                TargetTypeFullNames = [AnyEnumTarget],
            },

            // Community — JsonStringEnumMemberConverter
            [JsonStringEnumMemberConverter] = new ConverterSchemaHint
            {
                SchemaType = JsonSchemaType.String,
                TargetTypeFullNames = [AnyEnumTarget],
            },

            // Newtonsoft.Json — StringEnumConverter
            [NewtonsoftStringEnumConverter] = new ConverterSchemaHint
            {
                SchemaType = JsonSchemaType.String,
                TargetTypeFullNames = [AnyEnumTarget],
            },

            // Newtonsoft.Json — IsoDateTimeConverter
            [IsoDateTimeConverter] = new ConverterSchemaHint
            {
                SchemaType = JsonSchemaType.String,
                Format = "date-time",
                TargetTypeFullNames = ["System.DateTime", "System.DateTimeOffset"],
            },

            // Newtonsoft.Json — JavaScriptDateTimeConverter
            [JavaScriptDateTimeConverter] = new ConverterSchemaHint
            {
                SchemaType = JsonSchemaType.String,
                Format = "date-time",
                TargetTypeFullNames = ["System.DateTime", "System.DateTimeOffset"],
            },

            // Newtonsoft.Json — UnixDateTimeConverter
            [UnixDateTimeConverter] = new ConverterSchemaHint
            {
                SchemaType = JsonSchemaType.Integer,
                Format = "int64",
                Description = "Unix timestamp (seconds)",
                TargetTypeFullNames = ["System.DateTime", "System.DateTimeOffset"],
            },
        };

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a <see cref="ConverterSchemaHint"/> for the given converter type full name,
    /// or <see langword="null"/> if the converter is not in the registry.
    /// </summary>
    /// <remarks>
    /// Handles closed-generic forms transparently: e.g.
    /// <c>System.Text.Json.Serialization.JsonStringEnumConverter`1[[MyEnum, ...]]</c>
    /// is normalised to the open-generic key before lookup.
    /// </remarks>
    /// <param name="converterFullName">
    /// The <see cref="Type.FullName"/> of the converter type as returned by
    /// MetadataLoadContext reflection.
    /// </param>
    public static ConverterSchemaHint? TryGet(string converterFullName)
    {
        if (string.IsNullOrEmpty(converterFullName))
            return null;

        // First: exact match.
        if (Registry.TryGetValue(converterFullName, out var hint))
            return hint;

        // Second: normalise closed-generic instantiation → open-generic definition key.
        // E.g. "...JsonStringEnumConverter`1[[SomeEnum, Assembly, ...]]" → "...JsonStringEnumConverter`1"
        var bracketIndex = converterFullName.IndexOf('[');
        if (bracketIndex > 0)
        {
            var openGenericName = converterFullName[..bracketIndex];
            if (Registry.TryGetValue(openGenericName, out hint))
                return hint;
        }

        // Third: short-name fallback for degraded-compilation scenarios where the semantic
        // model cannot resolve the FQN and produces only the bare class name
        // (e.g. "JsonStringEnumConverter" instead of the full namespace-qualified name).
        // Only triggers when the input has no '.' — i.e. it is already a bare short name.
        // For FQN inputs (containing '.'), we never fall back to short-name matching to
        // prevent false positives across namespaces.
        if (converterFullName.IndexOf('.') < 0)
        {
            ConverterSchemaHint? match = null;
            foreach (var (key, value) in Registry)
            {
                var lastDot = key.LastIndexOf('.');
                var keyShort = lastDot < 0 ? key : key[(lastDot + 1)..];
                if (string.Equals(keyShort, converterFullName, StringComparison.Ordinal))
                {
                    if (match != null) return null; // ambiguous — do not guess
                    match = value;
                }
            }
            return match;
        }

        return null;
    }

    /// <summary>
    /// Returns the first matching <see cref="ConverterSchemaHint"/> from the provided
    /// converter full names, or <see langword="null"/> if none are known.
    /// </summary>
    /// <param name="converterFullNames">
    /// One or more converter type full names to look up, in priority order.
    /// </param>
    public static ConverterSchemaHint? TryGetAny(params string[] converterFullNames)
    {
        foreach (var name in converterFullNames)
        {
            var hint = TryGet(name);
            if (hint != null)
                return hint;
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the given hint applies to the specified target type.
    /// </summary>
    /// <param name="hint">The hint whose <see cref="ConverterSchemaHint.TargetTypeFullNames"/> to check.</param>
    /// <param name="isEnum">Whether the target type is an enum.</param>
    /// <param name="targetTypeFullName">The full name of the target type.</param>
    public static bool AppliesToType(ConverterSchemaHint hint, bool isEnum, string? targetTypeFullName)
    {
        // Empty means "applies to any type".
        if (hint.TargetTypeFullNames.Count == 0)
            return true;

        foreach (var target in hint.TargetTypeFullNames)
        {
            if (target == AnyEnumTarget && isEnum)
                return true;
            if (target == targetTypeFullName)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Describes how a known JSON converter affects the OpenAPI schema of the type it is applied to.
/// </summary>
public sealed record ConverterSchemaHint
{
    /// <summary>
    /// The OpenAPI schema type that replaces the default type derived from the C# type.
    /// </summary>
    public required JsonSchemaType SchemaType { get; init; }

    /// <summary>
    /// Optional OpenAPI format string (e.g. <c>"date-time"</c>, <c>"int64"</c>).
    /// When <see langword="null"/>, the format is left unset.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// Optional description to add to the schema when no other description has been set.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Full type names (or special markers) indicating which target types this converter
    /// applies to when used as a global converter.
    /// <para>
    /// An empty list means "applicable to any type".
    /// The special marker <see cref="JsonConverterRegistry.AnyEnumTarget"/> (<c>"enum"</c>)
    /// matches any enum type.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> TargetTypeFullNames { get; init; } = [];
}
