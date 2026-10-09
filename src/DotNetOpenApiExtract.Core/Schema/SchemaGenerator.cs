using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using DotNetOpenApiExtract.Core.Documentation;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Versioning;
using DotNetOpenApiExtract.Core.Diagnostics;
using System.Globalization;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Generates OpenAPI schemas from .NET types loaded via MetadataLoadContext.
/// Maintains a schema repository for $ref deduplication.
/// </summary>
/// <remarks>
/// All type inspection uses FullName string comparisons rather than typeof() or
/// IsAssignableTo() calls, which are unsafe against MetadataLoadContext-hosted types.
/// Enum values are represented as <see cref="JsonNode"/> instances (IList&lt;JsonNode&gt;)
/// as required by Microsoft.OpenApi v3.5.0.
/// </remarks>
public sealed class SchemaGenerator
{
    private readonly Dictionary<string, OpenApiSchema> _schemas = new(StringComparer.Ordinal);
    private readonly SchemaIdRegistry _schemaIds; // every component id, unique per (type, context, role); shared by the contexts of a build
    private readonly SchemaContext _context; // the serialization context this generator describes
    private readonly SchemaGenerator? _mvcCounterpart; // HTTP context only: the MVC generator of the same build
    private readonly Dictionary<string, Type> _schemaIdToType = new(StringComparer.Ordinal); // schema ID → original Type
    private readonly HashSet<string> _generating = new(StringComparer.Ordinal); // cycle detection
    private readonly SchemaOptions _options;
    private readonly DocumentationResolver? _docResolver;
    private readonly DiagnosticBag _diagnostics; // per-instance deduplication of warnings
    private readonly Dictionary<string, PolymorphismInfo?> _polymorphism = new(StringComparer.Ordinal); // base type → union, cached
    private readonly List<PendingLoss> _pendingLosses = []; // direct generation: delivered at the end of the public call
    private LossLedger? _ledger; // document build: the build's ledger
    private int _generationDepth; // public GenerateSchema nesting, to find the end of the outermost call
    private bool _hasNumberHandlingScope; // a property or type sets the number handling for the schema being generated
    private JsonNumberHandling _numberHandlingScope;
    private bool _bindingByMemberName; // a parameter outside a JSON body: model binding reads enum member names

    // Cache for NullableContextAttribute per declaring type — avoids repeated
    // GetCustomAttributesData() scans on the same type when processing its properties.
    // Key: type.FullName ?? type.Name (MetadataLoadContext types are not reliably equality-comparable).
    // Null value means "attribute not present".
    private readonly Dictionary<string, byte?> _nullableContextByType = new(StringComparer.Ordinal);

    // -------------------------------------------------------------------------
    // Primitive FullName → (type, format) look-up table
    // -------------------------------------------------------------------------
    private static readonly Dictionary<string, (JsonSchemaType SchemaType, string? Format)> PrimitiveMap =
        new(StringComparer.Ordinal)
        {
            ["System.String"]        = (JsonSchemaType.String,  null),
            ["System.Char"]          = (JsonSchemaType.String,  null),
            ["System.Boolean"]       = (JsonSchemaType.Boolean, null),

            // Integer types
            ["System.Byte"]          = (JsonSchemaType.Integer, "int32"),
            ["System.SByte"]         = (JsonSchemaType.Integer, "int32"),
            ["System.Int16"]         = (JsonSchemaType.Integer, "int32"),
            ["System.UInt16"]        = (JsonSchemaType.Integer, "int32"),
            ["System.Int32"]         = (JsonSchemaType.Integer, "int32"),
            ["System.UInt32"]        = (JsonSchemaType.Integer, "int64"), // values above Int32.MaxValue
            ["System.Int64"]         = (JsonSchemaType.Integer, "int64"),
            ["System.UInt64"]        = (JsonSchemaType.Integer, null), // no format holds ulong.MaxValue

            // Number types
            ["System.Single"]        = (JsonSchemaType.Number, "float"),
            ["System.Double"]        = (JsonSchemaType.Number, "double"),
            ["System.Decimal"]       = (JsonSchemaType.Number, "double"),
            ["System.Half"]          = (JsonSchemaType.Number, "float"),

            // Date/time types
            ["System.DateTime"]      = (JsonSchemaType.String, "date-time"),
            ["System.DateTimeOffset"]= (JsonSchemaType.String, "date-time"),
            ["System.DateOnly"]      = (JsonSchemaType.String, "date"),
            ["System.TimeOnly"]      = (JsonSchemaType.String, "time"),
            ["System.TimeSpan"]      = (JsonSchemaType.String, "duration"),

            // Well-known string types
            ["System.Guid"]          = (JsonSchemaType.String, "uuid"),
            ["System.Uri"]           = (JsonSchemaType.String, "uri"),

            // object / dynamic → empty schema (any type)
            ["System.Object"]        = (JsonSchemaType.Object, null), // written unconstrained: {} (see below)
        };

    // FullName prefix for Nullable<T>
    private const string NullableGenericFullName = "System.Nullable`1";

    // FullName for byte[]
    private const string ByteArrayFullName = "System.Byte[]";

    // Generic collection FullNames whose generic definition we match
    private static readonly HashSet<string> SetGenericDefinitions = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Generic.ISet`1",
        "System.Collections.Generic.IReadOnlySet`1",
        "System.Collections.Generic.SortedSet`1",
    };

    private static readonly HashSet<string> ListGenericDefinitions = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.List`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.Queue`1",
        "System.Collections.Generic.Stack`1",
        "System.Collections.Generic.LinkedList`1",
        "System.Collections.ObjectModel.Collection`1",
        "System.Collections.ObjectModel.ReadOnlyCollection`1",
        "System.Collections.ObjectModel.ObservableCollection`1",
    };

    private static readonly HashSet<string> DictionaryGenericDefinitions = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.IDictionary`2",
        "System.Collections.Generic.IReadOnlyDictionary`2",
        "System.Collections.Generic.SortedDictionary`2",
        "System.Collections.Generic.SortedList`2",
    };

    /// <summary>
    /// Initializes a new <see cref="SchemaGenerator"/> with optional configuration.
    /// </summary>
    /// <param name="options">Schema generation options. Uses defaults when <see langword="null"/>.</param>
    /// <param name="docResolver">
    /// Optional documentation resolver. When supplied, enum schemas will include an
    /// <c>x-enum-descriptions</c> extension populated from XML <c>&lt;summary&gt;</c> tags.
    /// </param>
    /// <exception cref="OpenApiConfigurationException">
    /// <see cref="SchemaOptions.OpenApiVersion"/> is not 3.0, 3.1 or 3.2.
    /// </exception>
    public SchemaGenerator(SchemaOptions? options = null, DocumentationResolver? docResolver = null)
        : this(options, docResolver, new SchemaIdRegistry(), SchemaContext.Mvc, mvcCounterpart: null)
    {
    }

    /// <summary>
    /// A generator for the HTTP serialization context of a build whose HTTP JSON options differ from
    /// the MVC ones. It shares the id registry of <paramref name="mvcCounterpart"/>, so no id of one
    /// context is ever given to the other; a type the MVC context already describes gets the
    /// candidate id with the suffix <c>Http</c>, a type only this context describes keeps its usual id.
    /// </summary>
    internal static SchemaGenerator ForHttpContext(
        SchemaOptions options, DocumentationResolver? docResolver, SchemaGenerator mvcCounterpart)
    {
        ArgumentNullException.ThrowIfNull(mvcCounterpart);
        var generator = new SchemaGenerator(options, docResolver, mvcCounterpart._schemaIds, SchemaContext.Http, mvcCounterpart);
        if (mvcCounterpart._ledger != null)
            generator.AttachLedger(mvcCounterpart._ledger);
        return generator;
    }

    private SchemaGenerator(
        SchemaOptions? options,
        DocumentationResolver? docResolver,
        SchemaIdRegistry schemaIds,
        SchemaContext context,
        SchemaGenerator? mvcCounterpart)
    {
        _options = options ?? new SchemaOptions();
        TargetVersion.EnsureSupported(_options.OpenApiVersion, nameof(SchemaOptions.OpenApiVersion));
        _diagnostics = new DiagnosticBag(_options.OnDiagnostic);
        _docResolver = docResolver;
        _schemaIds = schemaIds;
        _context = context;
        _mvcCounterpart = mvcCounterpart;
    }

    /// <summary>All generated component schemas (for the components/schemas section).</summary>
    public IReadOnlyDictionary<string, OpenApiSchema> Schemas => _schemas;

    /// <summary>Maps schema IDs to their original .NET Type (for documentation resolution).</summary>
    public IReadOnlyDictionary<string, Type> SchemaTypes => _schemaIdToType;

    /// <summary>
    /// Generate an OpenAPI schema for <paramref name="type"/>. Returns a $ref schema
    /// (as <see cref="OpenApiSchemaReference"/>) when the type is a complex object that has
    /// already been registered (or is currently being registered) in the schema repository.
    /// Primitive and collection types are returned as inline schemas.
    /// </summary>
    /// <param name="type">
    /// A <see cref="Type"/> instance loaded via MetadataLoadContext (reflection-only context).
    /// </param>
    /// <returns>
    /// An <see cref="IOpenApiSchema"/> that is either an inline <see cref="OpenApiSchema"/>
    /// or an <see cref="OpenApiSchemaReference"/>.
    /// </returns>
    public IOpenApiSchema GenerateSchema(Type type)
    {
        _generationDepth++;
        try
        {
            var schema = GenerateSchemaCore(type);
            if (_generationDepth == 1 && _pendingLosses.Count > 0)
            {
                // Direct generation (no document build): deliver this call's warnings for what
                // the requested schema reaches, deduplicated per generator instance.
                var losses = _pendingLosses.ToList();
                _pendingLosses.Clear();
                DownlevelPass.RunForSchemas(
                    losses, _options.OpenApiVersion,
                    _schemas.ToDictionary(s => s.Key, s => (IOpenApiSchema)s.Value, StringComparer.Ordinal),
                    [schema], _diagnostics);
            }

            return schema;
        }
        finally
        {
            _generationDepth--;
        }
    }

    /// <summary>
    /// The schema of a value ASP.NET Core model binding reads from the route, query string, a header
    /// or a form field. Such a value is converted by its type converter, not by the JSON serializer:
    /// an enum is read by its member names (and numbers), so a string enum lists the member names,
    /// not the names a JSON converter writes.
    /// </summary>
    internal IOpenApiSchema GenerateBoundValueSchema(Type type)
    {
        var previous = _bindingByMemberName;
        _bindingByMemberName = true;
        try
        {
            return GenerateSchema(type);
        }
        finally
        {
            _bindingByMemberName = previous;
        }
    }

    private IOpenApiSchema GenerateSchemaCore(Type type)
    {
        // --- 1. byte[] → base64 binary string (before the array check below) ---
        if (type.IsArray && type.GetElementType()?.FullName == "System.Byte")
            return VersionedSchemaForms.Base64(_options.OpenApiVersion);

        // --- 1b. File content (FileResult, IFileHttpResult, Stream, IFormFile) → binary string ---
        // Raw bytes on the wire, never a JSON object: no component for the framework type.
        if (FileTypes.IsFile(type))
            return new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };

        // --- 2. T[] (non-byte) → array schema ---
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            return new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = GenerateSchema(elementType),
            };
        }

        // --- 3. Nullable<T> → unwrap, generate for T, add Null to type flags ---
        if (IsNullableValueType(type))
        {
            var inner = GenerateSchema(type.GetGenericArguments()[0]);
            return inner is OpenApiSchema { Type: null, AnyOf.Count: > 0 } union
                ? VersionedSchemaForms.NullableUnion(union, _options.OpenApiVersion)
                : MakeNullable(inner);
        }

        // --- 3b. BCL JSON container types (JsonElement, JsonNode, JObject, etc.) ---
        // Must be checked before PrimitiveMap and generic-collection branches because
        // JsonObject implements IDictionary<string,JsonNode?> and JsonArray implements
        // IList<JsonNode?> — the collection branch would pick them up incorrectly.
        var bclTemplate = BclJsonTypeRegistry.TryGet(type.FullName ?? string.Empty);
        if (bclTemplate != null)
            return BclJsonTypeRegistry.CreateSchema(bclTemplate);

        // --- 4. Primitive types ---
        var fullName = type.FullName ?? string.Empty;

        // object / dynamic hold any JSON value: an unconstrained schema.
        if (fullName == "System.Object")
            return new OpenApiSchema();

        if (PrimitiveMap.TryGetValue(fullName, out var primitive))
        {
            // Check if a globally-registered converter overrides the default primitive schema.
            // This handles converters like IsoDateTimeConverter or UnixDateTimeConverter
            // registered globally via SchemaOptions.GlobalConverterTypeNames.
            // Enum types are excluded here — they are handled via GlobalEnumConverter.
            foreach (var converterFullName in _options.GlobalConverterTypeNames)
            {
                var converterHint = JsonConverterRegistry.TryGet(converterFullName);
                if (converterHint != null && JsonConverterRegistry.AppliesToType(converterHint, isEnum: false, type.FullName))
                    return BuildSchemaFromHint(converterHint);
            }

            var schema = new OpenApiSchema { Type = primitive.SchemaType };
            if (primitive.Format != null)
                schema.Format = primitive.Format;

            // Number handling (property > type > global) applies to numeric types only
            if (primitive.SchemaType == JsonSchemaType.Integer
                || primitive.SchemaType == JsonSchemaType.Number)
            {
                return ApplyNumberHandling(schema, fullName, EffectiveNumberHandling);
            }

            return schema;
        }

        // --- 5. Enum ---
        if (type.IsEnum)
            return GenerateEnumSchema(type);

        // --- 5b. IAsyncEnumerable<T> (or a type implementing it) → array of T ---
        // System.Text.Json writes an asynchronous sequence as a JSON array; there is no object
        // of the sequence type on the wire, so no component is created for it.
        if (StreamingTypes.TryGetAsyncEnumerableElementType(type, out var asyncElementType))
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = GenerateSchema(asyncElementType),
            };
        }

        // --- 6. Generic collections and dictionaries ---
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var genericDefFullName = genericDef.FullName ?? string.Empty;

            // Dictionary: key and value from the actual IDictionary<TKey, TValue> implementation,
            // not from the position of the type's own generic arguments.
            if ((DictionaryGenericDefinitions.Contains(genericDefFullName) || ImplementsDictionaryInterface(type))
                && DictionaryKeyValue(type) is var (keyType, valueType))
            {
                return new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    AdditionalProperties = GenerateSchema(valueType),
                    PropertyNames = KeySchema(keyType),
                };
            }

            // Set (uniqueItems: true)
            if (SetGenericDefinitions.Contains(genericDefFullName))
            {
                var itemType = type.GetGenericArguments()[0];
                return new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = GenerateSchema(itemType),
                    UniqueItems = true,
                };
            }

            // List / collection
            if (ListGenericDefinitions.Contains(genericDefFullName)
                || ImplementsEnumerableInterface(type))
            {
                var itemType = type.GetGenericArguments()[0];
                return new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = GenerateSchema(itemType),
                };
            }
        }

        // --- 7. Non-generic IEnumerable (e.g. ArrayList, IEnumerable) → array of any ---
        if (IsNonGenericEnumerable(type))
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema(), // any type items
            };
        }

        // --- 8. Complex types (class, struct, record, interface) ---
        // A property's number handling does not reach the members of a nested object.
        // Nor does binding by member name: a component describes the JSON form of its type.
        var (hadScope, scope, byMemberName) = (_hasNumberHandlingScope, _numberHandlingScope, _bindingByMemberName);
        _hasNumberHandlingScope = false;
        _bindingByMemberName = false;
        try
        {
            return GenerateComplexSchema(type);
        }
        finally
        {
            (_hasNumberHandlingScope, _numberHandlingScope, _bindingByMemberName) = (hadScope, scope, byMemberName);
        }
    }

    // =========================================================================
    // Enum schema
    // =========================================================================

    /// <summary>
    /// Generates a schema for an enum type, respecting <see cref="SchemaOptions.EnumAsString"/>,
    /// the presence of <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> on the type,
    /// and any applicable global converters in <see cref="SchemaOptions.GlobalConverterTypeNames"/>.
    /// Also emits <c>x-enum-descriptions</c>, <c>x-enum-varnames</c>, and a markdown
    /// auto-description when the doc resolver is available and options permit.
    /// </summary>
    private IOpenApiSchema GenerateEnumSchema(Type enumType, EnumWireNaming? propertyNaming = null)
    {
        // A string converter on the property names the members; otherwise the type's converters do.
        var naming = propertyNaming ?? TypeEnumNaming(enumType);
        bool asString = naming != null;

        var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);

        OpenApiSchema schema;

        if (asString)
        {
            var enumValues = fields
                .Select(f => (JsonNode)JsonValue.Create(EnumWireName(f, naming!))!)
                .ToList();

            schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = enumValues,
            };
        }
        else
        {
            // Numeric enum: collect underlying integer values
            var enumValues = fields
                .Select(EnumFieldValue)
                .ToList();

            // type / format as for a property of the underlying type (long / ulong values unclipped).
            var underlying = EnumUnderlyingTypeName(enumType);
            schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = PrimitiveMap.TryGetValue(underlying, out var primitive) ? primitive.Format : "int32",
                Enum = enumValues,
            };
        }

        // [Obsolete] on the enum type → deprecated: true
        if (AttributeHelper.HasAttribute(enumType, AttributeHelper.Names.Obsolete))
            schema.Deprecated = true;

        // Collect per-value descriptions and emit extensions + auto-description.
        ApplyEnumExtensions(schema, enumType, fields, naming);

        return schema;
    }

    /// <summary>
    /// Emits <c>x-enum-descriptions</c>, <c>x-enum-varnames</c>, and optionally a
    /// markdown auto-glue <c>description</c> on the enum schema.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>
    ///     <c>x-enum-varnames</c>: always emitted when <see cref="SchemaOptions.EnumVarnames"/> is
    ///     <see langword="true"/> and at least one field exists. Array length matches <c>enum[]</c>.
    ///   </item>
    ///   <item>
    ///     <c>x-enum-descriptions</c>: emitted only when at least one value has a non-empty
    ///     description (XML or <c>[Description]</c>). Array length matches <c>enum[]</c>; empty
    ///     string is used for undocumented values.
    ///   </item>
    ///   <item>
    ///     Auto-glue description: built only when <see cref="SchemaOptions.EnumAutoDescription"/> is
    ///     <see langword="true"/>, at least one value is documented, and <c>schema.Description</c>
    ///     was not set by a converter hint.
    ///   </item>
    /// </list>
    /// </remarks>
    private void ApplyEnumExtensions(
        OpenApiSchema schema,
        Type enumType,
        FieldInfo[] fields,
        EnumWireNaming? naming)
    {
        // x-enum-varnames — unconditional when enabled and fields exist.
        if (_options.EnumVarnames && fields.Length > 0)
        {
            schema.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
            var varnamesArr = new JsonArray();
            foreach (var f in fields)
                varnamesArr.Add(JsonValue.Create(f.Name));
            schema.Extensions["x-enum-varnames"] = new JsonNodeExtension(varnamesArr);
        }

        // x-enum-descriptions and auto-glue require a doc resolver.
        if (_docResolver == null)
            return;

        // Collect per-value descriptions.
        var descriptions = new List<string>(fields.Length);
        bool anyNonEmpty = false;
        foreach (var field in fields)
        {
            var desc = _docResolver.ResolveEnumValueDescription(enumType, field.Name);
            descriptions.Add(desc);
            if (!string.IsNullOrEmpty(desc))
                anyNonEmpty = true;
        }

        // x-enum-descriptions: only when at least one value is documented.
        if (anyNonEmpty)
        {
            schema.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
            var arr = new JsonArray();
            foreach (var d in descriptions)
                arr.Add(JsonValue.Create(d));
            schema.Extensions["x-enum-descriptions"] = new JsonNodeExtension(arr);
        }

        // Auto-glue markdown description.
        // Skip if the feature is disabled, no values are documented, or the description
        // was already set by a converter hint (non-null/non-empty schema.Description).
        if (!_options.EnumAutoDescription || !anyNonEmpty)
            return;

        // If a converter hint already wrote a description, preserve it — the hint wins.
        if (!string.IsNullOrEmpty(schema.Description))
            return;

        // Build the bullet list (only for documented values).
        var sb = new StringBuilder();
        for (int i = 0; i < fields.Length; i++)
        {
            var desc = descriptions[i];
            if (string.IsNullOrEmpty(desc))
                continue;

            // Determine the value representation for the bullet.
            string valueRepresentation;
            if (naming != null)
            {
                valueRepresentation = EnumWireName(fields[i], naming);
            }
            else
            {
                valueRepresentation = EnumFieldValue(fields[i])!.ToJsonString();
            }

            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append($"* `{valueRepresentation}` — {fields[i].Name}: {desc}");
        }

        // Retrieve the type-level summary to use as the intro.
        // Note: this is called here rather than relying on the document builder step
        // because we need it synchronously during schema construction.
        var typeSummary = _docResolver.ResolveTypeDescription(enumType);

        string finalDescription;
        if (!string.IsNullOrEmpty(typeSummary))
        {
            // Full format: intro + blank line + bullets
            finalDescription = typeSummary + "\n\n" + sb;
        }
        else
        {
            // Bullet list only — no intro
            finalDescription = sb.ToString();
        }

        if (!string.IsNullOrEmpty(finalDescription))
            schema.Description = finalDescription;
    }

    /// <summary>
    /// How the enum type is written when it is written as strings, or <see langword="null"/> for
    /// numbers: the converter on the type, else the first global converter that writes enums as
    /// strings, else <see cref="SchemaOptions.EnumAsString"/> (System.Text.Json's string converter).
    /// A parameter outside a JSON body is bound by its member name, whatever the converters say.
    /// </summary>
    private EnumWireNaming? TypeEnumNaming(Type enumType)
    {
        EnumWireNaming? naming = null;
        if (GetConverterHintForType(enumType, enumType.GetCustomAttributesData()) is { SchemaType: JsonSchemaType.String } typeHint)
            naming = typeHint.EnumNaming;
        else if (GlobalEnumConverter() is { } global)
            naming = global.Hint.EnumNaming with { Policy = global.Policy };
        else if (_options.EnumAsString)
            naming = EnumWireNaming.JsonStringEnumMemberName;

        return naming != null && _bindingByMemberName ? EnumWireNaming.MemberName : naming;
    }

    /// <summary>
    /// The naming of the enum a property's schema is built from (an enum or a nullable enum), or
    /// <see langword="null"/> when it is not a string enum: the property's converter when it applies,
    /// else the enum type's (<see cref="TypeEnumNaming"/>). It matches the names of the schema.
    /// </summary>
    private EnumWireNaming? PropertyEnumNaming(Type propertyType, IList<CustomAttributeData> attrData)
    {
        var enumType = propertyType.IsEnum ? propertyType
            : IsNullableValueType(propertyType) && propertyType.GetGenericArguments()[0].IsEnum ? propertyType.GetGenericArguments()[0]
            : null;
        if (enumType == null)
            return null;

        return GetConverterHintForType(propertyType, attrData) is { SchemaType: JsonSchemaType.String } propertyHint
            ? propertyHint.EnumNaming
            : TypeEnumNaming(enumType);
    }

    /// <summary>The name of the enum member <paramref name="field"/> on the wire under <paramref name="naming"/>.</summary>
    /// <remarks>The member attribute the converter reads wins; otherwise its naming policy is applied.</remarks>
    internal static string EnumWireName(FieldInfo field, EnumWireNaming naming)
    {
        var attributeName = naming.Rename switch
        {
            EnumMemberRename.JsonStringEnumMemberName => AttributeHelper.Names.JsonStringEnumMemberName,
            EnumMemberRename.EnumMemberValue          => AttributeHelper.Names.EnumMember,
            _                                         => null,
        };
        if (attributeName != null && AttributeHelper.GetAttribute(field, attributeName) is { } attribute)
        {
            var renamed = naming.Rename == EnumMemberRename.JsonStringEnumMemberName
                ? AttributeHelper.GetConstructorArgument<string>(attribute, 0)
                : AttributeHelper.GetNamedArgument<string>(attribute, "Value");
            if (renamed != null)
                return renamed;
        }

        return naming.Policy is { } policy and not JsonNamingPolicy.Preserve
            ? ApplyNamingPolicy(field.Name, policy)
            : field.Name;
    }

    /// <summary>
    /// Reads the integer value of an enum field using the RawConstantValue metadata.
    /// Falls back to field order index when the metadata is not available.
    /// </summary>
    /// <summary>
    /// The value of an enum member as a JSON number, without loss for every underlying type
    /// (<c>long</c> and <c>ulong</c> included).
    /// </summary>
    private static JsonNode EnumFieldValue(FieldInfo field)
    {
        object? raw;
        try
        {
            raw = field.GetRawConstantValue();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            raw = null;
        }

        return raw == null ? JsonValue.Create(0) : IntegralValue(raw);
    }

    /// <summary>
    /// An integral value (any integer type, or an enum member's raw value) as a JSON number without
    /// loss: <c>int</c> when it fits (as before), else <c>long</c>; a <c>ulong</c> above
    /// <c>long.MaxValue</c> goes through <c>decimal</c>, which holds it exactly — Microsoft.OpenApi
    /// writes no <c>ulong</c> value.
    /// </summary>
    internal static JsonNode IntegralValue(object raw) => raw switch
    {
        ulong ul when ul > long.MaxValue => JsonValue.Create((decimal)ul),
        _ => Convert.ToInt64(raw, CultureInfo.InvariantCulture) is var number && number is >= int.MinValue and <= int.MaxValue
            ? JsonValue.Create((int)number)
            : JsonValue.Create(number),
    };

    /// <summary>Full name of the underlying type of <paramref name="enumType"/> (from its <c>value__</c> field).</summary>
    private static string EnumUnderlyingTypeName(Type enumType) =>
        enumType.GetField("value__", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType.FullName
        ?? "System.Int32";

    /// <summary>
    /// Returns the <see cref="ConverterSchemaHint"/> for the converter declared on <paramref name="type"/>
    /// or in <paramref name="attrData"/> via <c>[JsonConverter(typeof(X))]</c>,
    /// or <see langword="null"/> when no such attribute is present or the converter is unknown.
    /// Logs a one-time warning per unknown converter.
    /// </summary>
    private ConverterSchemaHint? GetConverterHintForType(Type targetType, IList<CustomAttributeData> attrData)
    {
        var jsonConverterAttr = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.JsonConverter);
        if (jsonConverterAttr == null)
            return null;

        if (jsonConverterAttr.ConstructorArguments.Count == 0)
            return null;

        var arg = jsonConverterAttr.ConstructorArguments[0];
        if (arg.Value is not Type converterType)
            return null;

        var converterFullName = converterType.FullName ?? string.Empty;
        var hint = JsonConverterRegistry.TryGet(converterFullName);

        if (hint == null)
        {
            // One warning per unique unknown converter per generator instance (the bag deduplicates).
            var message = $"Unknown [JsonConverter]: {converterFullName} — schema unchanged.";
            _diagnostics.Report(new ExtractionDiagnostic
            {
                Code       = ExtractionDiagnosticCodes.SchemaUnknownJsonConverter,
                Message    = message,
                Subjects   = [converterFullName],
                StderrLine = "[DotNetOpenApiExtract] " + message,
            });
            return null;
        }

        // Verify that the hint applies to the target type.
        // A converter of T applies to a Nullable<T> member as well.
        var converted = IsNullableValueType(targetType) ? targetType.GetGenericArguments()[0] : targetType;
        if (!JsonConverterRegistry.AppliesToType(hint, converted.IsEnum, converted.FullName))
            return null;

        return hint;
    }

    /// <summary>
    /// The first globally registered converter (from <see cref="SchemaOptions.GlobalConverterTypeNames"/>)
    /// that applies to enum types, as System.Text.Json picks the first converter that can convert;
    /// <see langword="null"/> when there is none.
    /// </summary>
    private (ConverterSchemaHint Hint, JsonNamingPolicy? Policy)? GlobalEnumConverter()
    {
        for (var i = 0; i < _options.GlobalConverterTypeNames.Count; i++)
        {
            var hint = JsonConverterRegistry.TryGet(_options.GlobalConverterTypeNames[i]);
            if (hint == null) continue;
            if (JsonConverterRegistry.AppliesToType(hint, isEnum: true, targetTypeFullName: null))
                return (hint, i < _options.GlobalConverterEnumNamingPolicies.Count ? _options.GlobalConverterEnumNamingPolicies[i] : null);
        }

        return null;
    }

    /// <summary>
    /// Builds the scalar schema a converter writes for a non-enum type (<c>DateTime</c> under
    /// <c>IsoDateTimeConverter</c>, …): its type, format and description. String enums are built by
    /// <see cref="GenerateEnumSchema"/> with the converter's naming.
    /// </summary>
    private static IOpenApiSchema BuildSchemaFromHint(ConverterSchemaHint hint)
    {
        var schema = new OpenApiSchema { Type = hint.SchemaType };
        if (!string.IsNullOrEmpty(hint.Format))
            schema.Format = hint.Format;
        if (!string.IsNullOrEmpty(hint.Description))
            schema.Description = hint.Description;
        return schema;
    }

    // =========================================================================
    // Complex type schema
    // =========================================================================

    /// <summary>
    /// Generates an object schema for a complex type (class, struct, record) and stores it
    /// in the schema repository. Returns an <see cref="OpenApiSchemaReference"/> pointing to
    /// the schema. Uses cycle detection to handle recursive/self-referential types.
    /// </summary>
    private IOpenApiSchema GenerateComplexSchema(Type type)
    {
        if (UnionOf(type) is { } polymorphism)
            return GenerateUnionSchema(polymorphism);

        var schemaId = GetSchemaId(type);

        // If already fully generated, return $ref immediately.
        if (_schemas.ContainsKey(schemaId))
            return new OpenApiSchemaReference(schemaId, null);

        // Cycle detection: if currently being generated, return $ref to avoid infinite recursion.
        if (!_generating.Add(schemaId))
            return new OpenApiSchemaReference(schemaId, null);

        try
        {
            // Register an empty placeholder so recursive calls see the schema.
            var schema = new OpenApiSchema { Type = JsonSchemaType.Object };
            _schemas[schemaId] = schema;
            _schemaIdToType[schemaId] = type;

            PopulateObjectSchema(type, schema, schemaId);
            ApplyTypeExample(schema, type, schemaId);
            return new OpenApiSchemaReference(schemaId, null);
        }
        finally
        {
            _generating.Remove(schemaId);
        }
    }

    /// <summary>
    /// Fills <paramref name="schema"/> with the properties of <paramref name="type"/> as System.Text.Json
    /// writes them when the type is used directly (base properties flattened in).
    /// </summary>
    private void PopulateObjectSchema(Type type, OpenApiSchema schema, string componentId)
    {
        // Collect all properties including inherited ones.
        var allProperties = CollectProperties(type);

        // [JsonExtensionData]: the dictionary receives every member not declared on the type, so it
        // is not a property of its own but makes the object open to any additional value.
        var extensionData = ExtensionDataProperty(type, allProperties);

        // [JsonNumberHandling] on the type applies to its members unless they declare their own.
        var typeNumberHandling = NumberHandlingAttribute(type.GetCustomAttributesData());

        var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (propName, propType, propInfo) in allProperties)
        {
            // Cache attribute data once per property to avoid repeated GetCustomAttributesData() calls.
            // Positional record parameters store their default-target attributes on the primary-ctor
            // parameter, not the synthesized property — merge them in (see AttributeHelper).
            var propAttrData = AttributeHelper.GetMergedPropertyAttributes(propInfo);

            // Skip [JsonIgnore(Condition = Always)]
            if (ShouldIgnoreProperty(propAttrData))
                continue;

            if (extensionData != null && ReferenceEquals(propInfo, extensionData))
                continue;

            // Determine the serialized property name.
            var serializedName = ResolvePropertyName(propAttrData, propName);

            // Generate the property schema, with the number handling of the property, else of
            // the type, else the global one (System.Text.Json's precedence).
            var handling = NumberHandlingAttribute(propAttrData) ?? typeNumberHandling;
            IOpenApiSchema propSchema;
            var (hadScope, scope) = (_hasNumberHandlingScope, _numberHandlingScope);
            if (handling.HasValue)
                (_hasNumberHandlingScope, _numberHandlingScope) = (true, handling.Value);
            try
            {
                propSchema = GenerateSchema(propType);
            }
            finally
            {
                (_hasNumberHandlingScope, _numberHandlingScope) = (hadScope, scope);
            }

            // Apply property-level [JsonConverter] override.
            // This handles cases such as [JsonConverter(typeof(JsonStringEnumConverter))]
            // placed on a property whose enum type does not carry the converter itself.
            // On a Nullable<T> property the converter converts T (STJ and Newtonsoft both lift it):
            // the schema is the converter's form of T in the version's nullable form.
            var propConverterHint = GetConverterHintForType(propType, propAttrData);
            if (propConverterHint != null)
            {
                var underlying = IsNullableValueType(propType) ? propType.GetGenericArguments()[0] : null;
                var converted = underlying ?? propType;
                // A string enum is the enum's own schema under the converter's naming, so it keeps
                // x-enum-varnames / x-enum-descriptions (CLR members) parallel to the wire names.
                propSchema = converted.IsEnum && propConverterHint.SchemaType == JsonSchemaType.String
                    ? GenerateEnumSchema(converted, propConverterHint.EnumNaming)
                    : BuildSchemaFromHint(propConverterHint);
                if (underlying != null)
                    propSchema = MakeNullable(propSchema);
            }
            else if (propType.FullName == "System.String"
                     && AttributeHelper.HasAttribute(propAttrData, AttributeHelper.Names.Base64String))
            {
                // [Base64String] string: the value is base64 data, in the form of the target version.
                propSchema = VersionedSchemaForms.Base64(_options.OpenApiVersion);
            }

            // Apply nullable flag for reference type properties (matches Swashbuckle behavior).
            // Swashbuckle marks all reference type properties as nullable unless NRT
            // annotates them as non-nullable (byte=1).
            if (!propType.IsValueType && IsNullableReferenceProperty(propAttrData, propInfo))
            {
                propSchema = MakeNullable(propSchema);
            }

            // Apply validation and documentation attributes to the property schema.
            // OpenAPI forbids sibling keywords on a bare $ref (3.0) or treats them ambiguously (3.1).
            // When the property schema is a reference AND there are attributes that write sibling
            // keywords, wrap it in allOf so the keywords are valid on the enclosing schema.
            if (propSchema is OpenApiSchemaReference && HasAnySiblingAttribute(propAttrData))
                propSchema = EnsureMutableSchema(propSchema);

            if (propSchema is OpenApiSchema inlinePropSchema)
            {
                ApplyValidationAttributes(inlinePropSchema, propAttrData, propInfo, componentId);
                ApplyRange(inlinePropSchema, propAttrData, propInfo.DeclaringType?.FullName ?? componentId, propInfo.Name,
                    new LossAnchor.Node(new LossAnchor.Component(componentId), ["properties", serializedName]));
            }

            propSchema = ApplyAllowedAndDeniedValues(propSchema, propAttrData, new ValueSite(
                propType, propInfo.DeclaringType?.FullName ?? componentId, propInfo.Name,
                new LossAnchor.Node(new LossAnchor.Component(componentId), ["properties", serializedName]),
                PropertyEnumNaming(propType, propAttrData) ?? EnumWireNaming.MemberName));

            if (propSchema is OpenApiSchema withDefault)
                ApplyDefaultValue(withDefault, propAttrData, propInfo, serializedName, componentId);

            propSchema = ApplyPropertyExample(propSchema, type, propInfo, serializedName, componentId);

            properties[serializedName] = propSchema;

            // Mark as required if annotated or non-nullable (NRT).
            if (IsPropertyRequired(propAttrData, propType, propInfo))
                required.Add(serializedName);
        }

        schema.Properties = properties.Count > 0 ? properties : null;
        schema.Required = required.Count > 0 ? required : null;

        // [JsonUnmappedMemberHandling(Disallow)] on the type → additionalProperties: false
        ApplyTypeAttributes(schema, type);

        if (extensionData != null)
            schema.AdditionalProperties = new OpenApiSchema(); // any JSON value
    }

    private const string ObjectFullName = "System.Object";
    private const string JsonElementFullName = "System.Text.Json.JsonElement";
    private const string JsonObjectFullName = "System.Text.Json.Nodes.JsonObject";

    /// <summary>
    /// The <c>[JsonExtensionData]</c> property of <paramref name="type"/> (own or inherited, not
    /// <c>[JsonIgnore]</c>d), or <see langword="null"/>. Shapes System.Text.Json 10 rejects are
    /// extraction errors: a value type other than <c>object</c> / <c>JsonElement</c> or a key other than
    /// <c>string</c> (the property must be <c>JsonObject</c> or implement
    /// <c>IDictionary&lt;string, object&gt;</c> / <c>IDictionary&lt;string, JsonElement&gt;</c>), more than
    /// one such property, a property bound to a constructor parameter, and
    /// <c>[JsonUnmappedMemberHandling(Disallow)]</c> on the same type.
    /// </summary>
    private static PropertyInfo? ExtensionDataProperty(
        Type type, List<(string Name, Type PropertyType, PropertyInfo Info)> properties)
    {
        var candidates = properties
            .Where(p =>
            {
                var attributes = AttributeHelper.GetMergedPropertyAttributes(p.Info);
                return AttributeHelper.HasAttribute(attributes, AttributeHelper.Names.JsonExtensionData)
                    && !ShouldIgnoreProperty(attributes);
            })
            .ToList();
        if (candidates.Count == 0)
            return null;

        var typeName = type.FullName ?? type.Name;
        if (candidates.Count > 1)
        {
            throw new OpenApiExtractionException(
                $"{typeName} has more than one [JsonExtensionData] property ({string.Join(", ", candidates.Select(c => c.Name))}); " +
                "System.Text.Json allows one.",
                typeName, candidates[1].Name);
        }

        var (name, propertyType, info) = candidates[0];
        if (!IsValidExtensionDataType(propertyType))
        {
            throw new OpenApiExtractionException(
                $"{typeName}.{name} has [JsonExtensionData] but its type {propertyType.FullName} is not JsonObject or " +
                "a dictionary with string keys and object or JsonElement values; System.Text.Json rejects it.",
                typeName, name);
        }

        if (IsBoundToConstructorParameter(type, name))
        {
            throw new OpenApiExtractionException(
                $"{typeName}.{name} has [JsonExtensionData] and is bound to a constructor parameter; System.Text.Json rejects it.",
                typeName, name);
        }

        var unmapped = AttributeHelper.GetAttribute(type, AttributeHelper.Names.JsonUnmappedMemberHandling);
        if (unmapped != null && AttributeHelper.GetConstructorArgument<int>(unmapped, 0) == 1)
        {
            throw new OpenApiExtractionException(
                $"{typeName} is marked [JsonUnmappedMemberHandling(Disallow)] and has the [JsonExtensionData] property {name}; " +
                "System.Text.Json rejects the combination.",
                typeName, name);
        }

        return info;
    }

    private static bool IsValidExtensionDataType(Type type)
    {
        if (type.FullName == JsonObjectFullName)
            return true;

        return (type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces())
            .Any(i => i.IsGenericType
                      && i.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IDictionary`2"
                      && i.GetGenericArguments()[0].FullName == "System.String"
                      && i.GetGenericArguments()[1].FullName is ObjectFullName or JsonElementFullName);
    }

    /// <summary>
    /// Whether the constructor System.Text.Json deserializes <paramref name="type"/> with has a
    /// parameter matching <paramref name="propertyName"/> (case-insensitively): the
    /// <c>[JsonConstructor]</c> one, else none when a public parameterless constructor exists, else
    /// the only public constructor.
    /// </summary>
    private static bool IsBoundToConstructorParameter(Type type, string propertyName)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var chosen = constructors.FirstOrDefault(c => AttributeHelper.HasAttribute(c, AttributeHelper.Names.JsonConstructor));
        if (chosen == null)
        {
            var publicConstructors = constructors.Where(c => c.IsPublic).ToList();
            if (publicConstructors.Any(c => c.GetParameters().Length == 0) || publicConstructors.Count != 1)
                return false;
            chosen = publicConstructors[0];
        }

        return chosen.GetParameters().Any(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================================
    // Polymorphism
    // =========================================================================

    /// <summary>
    /// The polymorphism declared on <paramref name="type"/> (written as a union), or
    /// <see langword="null"/> for a type without it.
    /// </summary>
    private PolymorphismInfo? UnionOf(Type type)
    {
        var key = type.FullName ?? type.Name;
        if (!_polymorphism.TryGetValue(key, out var info))
        {
            info = PolymorphismReader.Read(type);
            _polymorphism[key] = info;
        }

        return info;
    }

    /// <summary>
    /// The component of a polymorphic base B, under B's own id so references at its uses stay
    /// unchanged. Alternatives:
    /// <list type="bullet">
    ///   <item>each derived type with a discriminator value → variant <c>{D}As{B}</c> (properties of D
    ///   plus the required discriminator property limited to the value);</item>
    ///   <item>each derived type without a value → its direct-use component (no discriminator);</item>
    ///   <item>a concrete B → the base branch <c>{B}Default</c>: B's own properties, plus — when every
    ///   alternative has a value — the constraint that keeps <c>oneOf</c> exclusive (no discriminator
    ///   property at all, or with <c>IgnoreUnrecognizedTypeDiscriminators</c> none of the mapped values).</item>
    /// </list>
    /// With a derived type without a value the alternatives are not mutually exclusive: <c>anyOf</c>,
    /// and a warning that they are distinguishable only by structure. Otherwise <c>oneOf</c>; an
    /// abstract base or interface also gets the <c>discriminator</c> object.
    /// </summary>
    private IOpenApiSchema GenerateUnionSchema(PolymorphismInfo polymorphism)
    {
        var baseType = polymorphism.BaseType;
        var unionId = GetSchemaId(baseType);

        if (_schemas.ContainsKey(unionId) || !_generating.Add(unionId))
            return new OpenApiSchemaReference(unionId, null);

        try
        {
            var union = new OpenApiSchema();
            _schemas[unionId] = union;
            _schemaIdToType[unionId] = baseType;

            var alternatives = new List<IOpenApiSchema>();
            var mapping = new Dictionary<string, OpenApiSchemaReference>(StringComparer.Ordinal);
            var mappedValues = new List<object>();
            var withoutValue = new List<Type>();

            foreach (var derived in polymorphism.DerivedTypes)
            {
                if (derived.DiscriminatorValue is null)
                {
                    // The base itself without a value is what the base branch already describes:
                    // no second alternative, no reference from the union to itself.
                    if (SameType(derived.Type, baseType))
                        continue;

                    withoutValue.Add(derived.Type);
                    alternatives.Add(GenerateDirectObjectSchema(derived.Type));
                    continue;
                }

                var variantId = ReserveVariantId(derived.Type, baseType, unionId);
                if (!_schemas.ContainsKey(variantId) && _generating.Add(variantId))
                {
                    try
                    {
                        var variant = new OpenApiSchema { Type = JsonSchemaType.Object };
                        _schemas[variantId] = variant;
                        _schemaIdToType[variantId] = derived.Type;
                        PopulateVariantSchema(variant, derived, polymorphism.PropertyName, variantId);
                    }
                    finally
                    {
                        _generating.Remove(variantId);
                    }
                }

                alternatives.Add(new OpenApiSchemaReference(variantId, null));
                mappedValues.Add(derived.DiscriminatorValue);
                mapping[Convert.ToString(derived.DiscriminatorValue, CultureInfo.InvariantCulture)!] =
                    new OpenApiSchemaReference(variantId, null);
            }

            var exclusive = withoutValue.Count == 0;
            var isConcrete = !baseType.IsAbstract && !baseType.IsInterface;
            OpenApiSchemaReference? baseBranch = null;
            if (isConcrete)
            {
                baseBranch = (OpenApiSchemaReference)GenerateBaseBranch(polymorphism, unionId, exclusive ? mappedValues : null);
                alternatives.Add(baseBranch);
            }

            if (exclusive)
            {
                union.OneOf = alternatives;
                ApplyDiscriminator(union, unionId, polymorphism, mapping, baseBranch);
            }
            else
            {
                union.AnyOf = alternatives;
                RecordLoss(new PendingLoss
                {
                    Class    = LossClass.Source,
                    Code     = ExtractionDiagnosticCodes.PolymorphismAnyOfWithoutDiscriminator,
                    Anchor   = new LossAnchor.Component(unionId),
                    Message  = $"derived types without a discriminator value ({string.Join(", ", withoutValue.Select(t => t.FullName))}) " +
                               "are distinguishable only by structure: written as anyOf without a discriminator object.",
                    Feature  = "schema.anyOf",
                    Subjects = withoutValue.Select(t => t.FullName ?? t.Name).ToList(),
                });
            }

            if (polymorphism.SwashbuckleDisagrees)
            {
                RecordLoss(new PendingLoss
                {
                    Class    = LossClass.Source,
                    Code     = ExtractionDiagnosticCodes.PolymorphismSourceDisagreement,
                    Anchor   = new LossAnchor.Component(unionId),
                    Message  = $"{baseType.FullName}: [SwaggerDiscriminator]/[SwaggerSubType] disagree with " +
                               "[JsonPolymorphic]/[JsonDerivedType]; the System.Text.Json attributes define the wire and are used.",
                    Feature  = "discriminator.source",
                    Subjects = [baseType.FullName ?? baseType.Name],
                });
            }

            return new OpenApiSchemaReference(unionId, null);
        }
        finally
        {
            _generating.Remove(unionId);
        }
    }

    /// <summary>
    /// The schema of <paramref name="type"/> as System.Text.Json writes it when it is not selected by a
    /// discriminator: a plain object of its properties, without any discriminator. For a type
    /// without polymorphism of its own that is its usual component; a type that is itself a
    /// polymorphic base gets a separate direct-use component <c>{T}Direct</c>, since its own id
    /// names its union (STJ ignores the type's own configuration when it is written as a derived
    /// type without a value of another base — measured on STJ 10).
    /// </summary>
    private IOpenApiSchema GenerateDirectObjectSchema(Type type)
    {
        if (UnionOf(type) == null)
            return GenerateComplexSchema(type);

        var fullName = (type.FullName ?? type.Name).Replace('.', '_').Replace('+', '_');
        var directId = _schemaIds.Reserve(SchemaKey.Direct(type, _context), $"{GetSchemaId(type)}Direct", $"{fullName}Direct{ContextSuffixFor(SchemaKey.Union(type))}");
        if (_schemas.ContainsKey(directId) || !_generating.Add(directId))
            return new OpenApiSchemaReference(directId, null);

        try
        {
            var schema = new OpenApiSchema { Type = JsonSchemaType.Object };
            _schemas[directId] = schema;
            _schemaIdToType[directId] = type;
            PopulateObjectSchema(type, schema, directId);
            ApplyTypeExample(schema, type, directId);
            return new OpenApiSchemaReference(directId, null);
        }
        finally
        {
            _generating.Remove(directId);
        }
    }

    private static bool SameType(Type left, Type right) =>
        string.Equals(left.FullName ?? left.Name, right.FullName ?? right.Name, StringComparison.Ordinal);

    /// <summary>
    /// The <c>discriminator</c> object of an exclusive union, written only when every alternative
    /// has a value, or — in 3.2 — when the only one without a value is the base branch, which
    /// <c>defaultMapping</c> then names. For a concrete base in 3.0/3.1 the discriminator property
    /// is optional, which those versions cannot express: no object, one warning on the union.
    /// </summary>
    private void ApplyDiscriminator(
        OpenApiSchema union,
        string unionId,
        PolymorphismInfo polymorphism,
        Dictionary<string, OpenApiSchemaReference> mapping,
        OpenApiSchemaReference? baseBranch)
    {
        if (baseBranch == null)
        {
            union.Discriminator = new OpenApiDiscriminator { PropertyName = polymorphism.PropertyName, Mapping = mapping };
            return;
        }

        if (_options.OpenApiVersion == OpenApiSpecVersion.OpenApi3_2)
        {
            union.Discriminator = new OpenApiDiscriminator
            {
                PropertyName   = polymorphism.PropertyName,
                Mapping        = mapping,
                DefaultMapping = new OpenApiSchemaReference(baseBranch.Reference.Id!, null),
            };
            return;
        }

        RecordLoss(new PendingLoss
        {
            Class           = LossClass.Degradation,
            Code            = ExtractionDiagnosticCodes.PolymorphismDiscriminatorNotExpressible,
            Anchor          = new LossAnchor.Component(unionId),
            Message         = $"the discriminator of concrete base {polymorphism.BaseType.FullName} is optional on the wire, " +
                              "which needs discriminator.defaultMapping (requires 3.2): written as oneOf without a discriminator object.",
            Feature         = "schema.discriminator",
            Action          = DiagnosticAction.Omitted,
            RequiredVersion = OpenApiSpecVersion.OpenApi3_2,
            Subjects        = [polymorphism.BaseType.FullName ?? polymorphism.BaseType.Name],
        });
    }

    /// <summary>
    /// The base branch <c>{B}Default</c> of a concrete base: B's own properties. With
    /// <paramref name="mappedValues"/> (every alternative has a value) it also excludes every
    /// object a variant takes: without <c>IgnoreUnrecognizedTypeDiscriminators</c>
    /// <c>not: {required: [property]}</c> (no discriminator at all — STJ rejects unknown values);
    /// with it, the discriminator, if present, must be a value STJ reads as a discriminator and none
    /// of the mapped values (<see cref="UnmappedDiscriminatorSchema"/>).
    /// </summary>
    private IOpenApiSchema GenerateBaseBranch(PolymorphismInfo polymorphism, string unionId, IReadOnlyList<object>? mappedValues)
    {
        var baseType = polymorphism.BaseType;
        var fullName = (baseType.FullName ?? baseType.Name).Replace('.', '_').Replace('+', '_');
        var branchId = _schemaIds.Reserve(SchemaKey.BaseDefault(baseType, _context), $"{unionId}Default", $"{fullName}Default{ContextSuffixFor(SchemaKey.Union(baseType))}");

        if (_schemas.ContainsKey(branchId) || !_generating.Add(branchId))
            return new OpenApiSchemaReference(branchId, null);

        try
        {
            var branch = new OpenApiSchema { Type = JsonSchemaType.Object };
            _schemas[branchId] = branch;
            _schemaIdToType[branchId] = baseType;
            PopulateObjectSchema(baseType, branch, branchId);

            if (mappedValues != null)
            {
                if (polymorphism.IgnoreUnrecognizedTypeDiscriminators)
                {
                    var properties = new Dictionary<string, IOpenApiSchema>(branch.Properties ?? new Dictionary<string, IOpenApiSchema>(), StringComparer.Ordinal)
                    {
                        [polymorphism.PropertyName] = UnmappedDiscriminatorSchema(mappedValues),
                    };
                    branch.Properties = properties;
                }
                else
                {
                    branch.Not = new OpenApiSchema { Required = new HashSet<string>(StringComparer.Ordinal) { polymorphism.PropertyName } };
                }
            }

            return new OpenApiSchemaReference(branchId, null);
        }
        finally
        {
            _generating.Remove(branchId);
        }
    }

    /// <summary>
    /// The discriminator values the base branch takes when unrecognized values are read as the base:
    /// what System.Text.Json reads as a type discriminator — any JSON string or an integer within
    /// Int32 range, whatever the declared values' type (measured on STJ 10: <c>true</c>, <c>null</c>,
    /// <c>1.5</c>, objects, arrays and integers outside Int32 are rejected) — except the mapped values.
    /// Mapping is type-sensitive (the number <c>1</c> does not select a variant declared with
    /// <c>"1"</c>), and so is <c>enum</c>. Written as <c>anyOf</c> rather than a type array, so the same
    /// form is valid in 3.0.
    /// </summary>
    private static OpenApiSchema UnmappedDiscriminatorSchema(IReadOnlyList<object> mappedValues) => new()
    {
        AnyOf =
        [
            new OpenApiSchema { Type = JsonSchemaType.String },
            new OpenApiSchema
            {
                Type    = JsonSchemaType.Integer,
                Format  = "int32",
                Minimum = int.MinValue.ToString(CultureInfo.InvariantCulture),
                Maximum = int.MaxValue.ToString(CultureInfo.InvariantCulture),
            },
        ],
        Not = new OpenApiSchema { Enum = mappedValues.Select(DiscriminatorValueNode).ToList() },
    };

    private static JsonNode DiscriminatorValueNode(object value) => value switch
    {
        int number => JsonValue.Create(number),
        _          => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))!,
    };

    /// <summary>
    /// Keeps a warning anchored on a component: in a document build it joins the build's ledger;
    /// in direct generation it is delivered at the end of the public call, if reachable.
    /// </summary>
    private void RecordLoss(PendingLoss loss)
    {
        if (_ledger != null)
            _ledger.Add(loss);
        else
            _pendingLosses.Add(loss);
    }

    /// <summary>
    /// Attaches the ledger of a document build: warnings are then delivered by the build after the
    /// document is final, filtered by the document's reachability.
    /// </summary>
    internal void AttachLedger(LossLedger ledger) => _ledger = ledger;

    /// <summary>
    /// The derived type's properties with the discriminator property first, required and limited
    /// to the variant's value. A derived property serialized under the discriminator's name is a
    /// contract System.Text.Json rejects, so it is an extraction error.
    /// </summary>
    private void PopulateVariantSchema(OpenApiSchema variant, DerivedTypeInfo derived, string propertyName, string variantId)
    {
        PopulateObjectSchema(derived.Type, variant, variantId);

        if (variant.Properties != null && variant.Properties.ContainsKey(propertyName))
        {
            var member = CollectProperties(derived.Type)
                .Where(p => ResolvePropertyName(AttributeHelper.GetMergedPropertyAttributes(p.Info), p.Name) == propertyName)
                .Select(p => p.Info.Name)
                .FirstOrDefault()
                ?? propertyName;
            throw new OpenApiExtractionException(
                $"{derived.Type.FullName}.{member} is serialized as '{propertyName}', the discriminator property " +
                "of its polymorphic base; System.Text.Json rejects this contract. Rename the property or the discriminator.",
                derived.Type.FullName ?? derived.Type.Name,
                member);
        }

        var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            [propertyName] = VersionedSchemaForms.SingleValue(derived.DiscriminatorValue!, _options.OpenApiVersion),
        };
        foreach (var (name, schema) in variant.Properties ?? new Dictionary<string, IOpenApiSchema>())
            properties[name] = schema;
        variant.Properties = properties;

        var required = new HashSet<string>(StringComparer.Ordinal) { propertyName };
        foreach (var name in variant.Required ?? new HashSet<string>())
            required.Add(name);
        variant.Required = required;
    }

    // =========================================================================
    // Validation attribute application
    // =========================================================================

    /// <summary>
    /// Applies validation and documentation attributes from the pre-fetched <paramref name="attrData"/>
    /// to the corresponding <paramref name="schema"/> keywords.
    /// Only inline <see cref="OpenApiSchema"/> instances are accepted; $ref wrappers must be
    /// unwrapped by the caller before calling this method. A property whose effective
    /// <c>readOnly</c> and <c>writeOnly</c> are both true is an extraction error (OpenAPI forbids both).
    /// </summary>
    private static void ApplyValidationAttributes(
        OpenApiSchema schema, IList<CustomAttributeData> attrData, PropertyInfo property, string componentId)
    {
        ApplyConstraintAttributes(schema, attrData);

        // [Obsolete] → deprecated: true
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Obsolete))
            schema.Deprecated = true;

        // [SwaggerSchema(Description)] → [Description] → [Display(Description)]: always wins over a
        // default set by a converter hint or the BCL registry, because a property-level annotation is
        // a direct user statement. The XML <summary> is applied later, only where none of them is set.
        var description = DocumentationResolver.AttributeDescription(attrData);
        if (description != null)
            schema.Description = description;

        ApplyAccessAndTitle(schema, attrData, property, componentId);
    }

    /// <summary>
    /// The value constraints of a property or a parameter: lengths (<c>[StringLength]</c>,
    /// <c>[MinLength]</c>, <c>[MaxLength]</c>, <c>[Length]</c>), <c>[RegularExpression]</c> and the
    /// format by its priority. <c>[Range]</c> is applied by <see cref="ApplyRange"/>.
    /// </summary>
    private static void ApplyConstraintAttributes(OpenApiSchema schema, IList<CustomAttributeData> attrData)
    {
        // [StringLength(maxLength, MinimumLength = minLength)]
        var stringLength = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.StringLength);
        if (stringLength != null)
        {
            var maxLen = AttributeHelper.GetConstructorArgument<int>(stringLength, 0);
            if (maxLen > 0) schema.MaxLength = Lower(schema.MaxLength, maxLen);
            var minLen = AttributeHelper.GetNamedArgument<int>(stringLength, "MinimumLength");
            if (minLen > 0) schema.MinLength = Higher(schema.MinLength, minLen);
        }

        // [MaxLength(n)] → maxLength (strings), maxItems (arrays) or maxProperties (dictionaries)
        var maxLength = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.MaxLength);
        if (maxLength != null)
        {
            var n = AttributeHelper.GetConstructorArgument<int>(maxLength, 0);
            if (n > 0) SetMaximumLength(schema, n);
        }

        // [MinLength(n)] → minLength (strings), minItems (arrays) or minProperties (dictionaries)
        var minLength = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.MinLength);
        if (minLength != null)
        {
            var n = AttributeHelper.GetConstructorArgument<int>(minLength, 0);
            if (n > 0) SetMinimumLength(schema, n);
        }

        // [Length(min, max)] → both bounds, by the same shape rule. A declaration LengthAttribute
        // rejects (negative minimum, maximum below minimum) constrains nothing.
        var length = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.Length);
        if (length != null)
        {
            var min = AttributeHelper.GetConstructorArgument<int>(length, 0);
            var max = AttributeHelper.GetConstructorArgument<int>(length, 1);
            if (min >= 0 && max >= min)
            {
                if (min > 0) SetMinimumLength(schema, min);
                SetMaximumLength(schema, max);
            }
        }

        // [RegularExpression(@"pattern")]
        var regex = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.RegularExpression);
        if (regex != null)
        {
            var pattern = AttributeHelper.GetConstructorArgument<string>(regex, 0);
            if (!string.IsNullOrEmpty(pattern)) schema.Pattern = pattern;
        }

        // format: one winner — [SwaggerSchema(Format)], else a profile attribute, else [DataType],
        // else the format derived from the type, which a source without a format leaves in place.
        // With number handling the format belongs to the numeric branch, where the type's format is.
        var format = DeclaredFormat(attrData);
        if (format != null)
            (NumberUnionBranches(schema)?.Number ?? schema).Format = format;
    }

    /// <summary>
    /// The validation attributes of an action parameter, as on a DTO property: lengths,
    /// <c>[RegularExpression]</c>, the format, <c>[Range]</c>, <c>[AllowedValues]</c> and
    /// <c>[DeniedValues]</c> (same keywords and version forms). A
    /// reference is wrapped in <c>allOf</c> first. A <c>[Range]</c> <c>RangeAttribute</c> rejects is an
    /// extraction error naming <paramref name="typeName"/> and <paramref name="memberName"/>; a bound
    /// that cannot be written gives a warning at <paramref name="anchor"/>.
    /// </summary>
    internal IOpenApiSchema ApplyParameterValidation(
        IOpenApiSchema schema, IList<CustomAttributeData> attrData, Type parameterType, string typeName, string memberName, LossAnchor anchor)
    {
        var constrains = AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.StringLength)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.MinLength)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.MaxLength)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Length)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.RegularExpression)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Range)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.AllowedValues)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.DeniedValues)
            || DeclaredFormat(attrData) != null;
        if (!constrains)
            return schema;

        var mutable = EnsureMutableSchema(schema);
        ApplyConstraintAttributes(mutable, attrData);
        ApplyRange(mutable, attrData, typeName, memberName, anchor);

        // A parameter is bound by member name (model binding), so its enum values are member names.
        return ApplyAllowedAndDeniedValues(mutable, attrData,
            new ValueSite(parameterType, typeName, memberName, anchor, EnumWireNaming.MemberName));
    }

    /// <summary><c>maxLength</c> (strings), <c>maxItems</c> (arrays) or <c>maxProperties</c> (dictionaries); the tighter bound wins.</summary>
    /// <remarks>
    /// Several length attributes on one member constrain together: the bound kept is the tighter one
    /// (the highest minimum, the lowest maximum). Bounds that contradict each other are written as
    /// they result, never repaired.
    /// </remarks>
    private static void SetMaximumLength(OpenApiSchema schema, int n)
    {
        if (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Array))
            schema.MaxItems = Lower(schema.MaxItems, n);
        else if (IsDictionarySchema(schema))
            schema.MaxProperties = Lower(schema.MaxProperties, n);
        else
            schema.MaxLength = Lower(schema.MaxLength, n);
    }

    private static int Lower(int? current, int n) => current is { } c && c < n ? c : n;

    private static int Higher(int? current, int n) => current is { } c && c > n ? c : n;

    /// <summary><c>minLength</c> (strings), <c>minItems</c> (arrays) or <c>minProperties</c> (dictionaries); the tighter bound wins.</summary>
    private static void SetMinimumLength(OpenApiSchema schema, int n)
    {
        if (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Array))
            schema.MinItems = Higher(schema.MinItems, n);
        else if (IsDictionarySchema(schema))
            schema.MinProperties = Higher(schema.MinProperties, n);
        else
            schema.MinLength = Higher(schema.MinLength, n);
    }

    /// <summary>
    /// The format the attributes of a property declare, by priority: <c>[SwaggerSchema(Format)]</c>,
    /// then <c>[EmailAddress]</c> / <c>[Url]</c> / <c>[Phone]</c>, then <c>[DataType]</c> by
    /// <see cref="DataTypeFormat"/>; <see langword="null"/> when none of them sets a format, so the
    /// format derived from the type stays.
    /// </summary>
    private static string? DeclaredFormat(IList<CustomAttributeData> attrData)
    {
        var swaggerSchema = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.SwaggerSchema);
        if (swaggerSchema != null
            && AttributeHelper.GetNamedArgument<string>(swaggerSchema, "Format") is { Length: > 0 } explicitFormat)
            return explicitFormat;

        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.EmailAddress))
            return "email";
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Url))
            return "uri";
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Phone))
            return "phone";

        var dataType = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.DataType);
        return dataType != null ? DataTypeFormat(dataType) : null;
    }

    /// <summary>
    /// The format of <c>[DataType(member)]</c>: <c>DateTime</c> → <c>date-time</c>, <c>Date</c> →
    /// <c>date</c>, <c>Time</c> → <c>time</c>, <c>Duration</c> → <c>duration</c>, <c>EmailAddress</c> →
    /// <c>email</c>, <c>Password</c> → <c>password</c>, <c>Url</c> / <c>ImageUrl</c> → <c>uri</c>,
    /// <c>PhoneNumber</c> → <c>phone</c>, <c>Upload</c> → <c>binary</c>. Any other member, and the
    /// custom-name constructor, gives no format.
    /// </summary>
    private static string? DataTypeFormat(CustomAttributeData dataType)
    {
        if (dataType.ConstructorArguments is not [{ ArgumentType: var argumentType, Value: { } raw }]
            || argumentType.FullName != AttributeHelper.Names.DataTypeEnum)
            return null;

        // The member is named by its constant: the attribute stores only the number.
        var member = argumentType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(f => Equals(f.GetRawConstantValue(), raw))?.Name;
        return member switch
        {
            "DateTime"              => "date-time",
            "Date"                  => "date",
            "Time"                  => "time",
            "Duration"              => "duration",
            "EmailAddress"          => "email",
            "Password"              => "password",
            "Url" or "ImageUrl"     => "uri",
            "PhoneNumber"           => "phone",
            "Upload"                => "binary",
            _                       => null,
        };
    }

    /// <summary>
    /// <c>readOnly</c>, <c>writeOnly</c> and <c>title</c>. The effective <c>readOnly</c> is
    /// <c>[SwaggerSchema(ReadOnly = …)]</c> when it sets the value, explicit <c>false</c> included,
    /// else <c>[ReadOnly(…)]</c>; <c>writeOnly</c> is <c>[SwaggerSchema(WriteOnly = …)]</c>. Only a true
    /// value is written; both true is an extraction error.
    /// </summary>
    private static void ApplyAccessAndTitle(
        OpenApiSchema schema, IList<CustomAttributeData> attrData, PropertyInfo property, string componentId)
    {
        var swaggerSchema = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.SwaggerSchema);
        bool readOnly = false, writeOnly = false, explicitReadOnly = false;
        if (swaggerSchema != null)
        {
            explicitReadOnly = AttributeHelper.TryGetNamedArgument(swaggerSchema, "ReadOnly", out readOnly);
            AttributeHelper.TryGetNamedArgument(swaggerSchema, "WriteOnly", out writeOnly);
            if (AttributeHelper.GetNamedArgument<string>(swaggerSchema, "Title") is { Length: > 0 } title)
                schema.Title = title;
        }

        if (!explicitReadOnly
            && AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.ReadOnly) is { } readOnlyAttribute)
            readOnly = AttributeHelper.GetConstructorArgument<bool>(readOnlyAttribute, 0);

        if (readOnly && writeOnly)
        {
            var typeName = property.DeclaringType?.FullName ?? componentId;
            throw new OpenApiExtractionException(
                $"{typeName}.{property.Name} is both read-only and write-only ([SwaggerSchema] / [ReadOnly]); " +
                "OpenAPI forbids a schema with both readOnly and writeOnly.",
                typeName, property.Name);
        }

        if (readOnly) schema.ReadOnly = true;
        if (writeOnly) schema.WriteOnly = true;
    }

    /// <summary>
    /// <c>[AllowedValues]</c> and <c>[DeniedValues]</c> on a property, combined with the schema's own
    /// constraints by logical AND. Allowed values: <c>enum</c> (one value: <c>const</c> for a string in
    /// 3.1+, otherwise a one-element <c>enum</c>), as a sibling keyword where the schema has no
    /// <c>enum</c> of its own; where it has (an enum type), or the schema is the <c>allOf</c> wrapper of
    /// a reference, the allowed values are a separate element of <c>allOf</c>: the type's own
    /// <c>enum</c> is kept, no intersection is computed. Denied values: <c>not: {enum}</c> as a sibling.
    /// Values not convertible to the schema's JSON type give a warning and no constraint.
    /// </summary>
    /// <summary>
    /// The member a value constraint is declared on: its CLR type, the names a warning gives
    /// (<c>Type.Member</c>), where the warning is located, and how its enum members are named.
    /// </summary>
    private sealed record ValueSite(Type MemberType, string TypeName, string MemberName, LossAnchor Anchor, EnumWireNaming EnumNaming);

    private IOpenApiSchema ApplyAllowedAndDeniedValues(
        IOpenApiSchema propSchema, IList<CustomAttributeData> attrData, ValueSite site)
    {
        var allowed = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.AllowedValues);
        var denied = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.DeniedValues);
        if ((allowed == null && denied == null) || propSchema is not OpenApiSchema schema)
            return propSchema;

        if (NumberUnionBranches(schema) is { } numberUnion)
        {
            ApplyValuesToNumberUnion(schema, numberUnion, allowed, denied, site);
            return schema;
        }

        var isWrapper = schema.AllOf is { Count: > 0 } && schema.AllOf[0] is OpenApiSchemaReference;
        var jsonType = isWrapper ? null : schema.Type;
        var propertyType = site.MemberType;
        var enumType = propertyType.IsEnum
            ? propertyType
            : IsNullableValueType(propertyType) && propertyType.GetGenericArguments()[0].IsEnum
                ? propertyType.GetGenericArguments()[0]
                : null;

        IOpenApiSchema result = schema;
        if (allowed != null && ConvertValues(allowed, jsonType, enumType, site, "AllowedValues") is { } allowedValues)
        {
            var constraint = allowedValues.Count == 1 ? SingleValueConstraint(allowedValues[0]) : new OpenApiSchema { Enum = allowedValues };
            if (isWrapper)
            {
                schema.AllOf!.Add(constraint);
            }
            else if (schema.Enum is { Count: > 0 } || !schema.Type.HasValue)
            {
                // The schema's own enum stays whole (and a composite such as the nullable form of a
                // reference stays as it is); the allowed values are a second schema to satisfy.
                result = new OpenApiSchema { AllOf = [schema, constraint] };
            }
            else if (constraint.Const != null)
            {
                schema.Const = constraint.Const;
            }
            else
            {
                schema.Enum = constraint.Enum;
            }
        }

        if (denied != null && ConvertValues(denied, jsonType, enumType, site, "DeniedValues") is { } deniedValues)
            ((OpenApiSchema)result).Not = new OpenApiSchema { Enum = deniedValues };

        return result;
    }

    /// <summary>The branches of a number-handling union (<see cref="ApplyNumberHandling"/>).</summary>
    private sealed record NumberUnion(OpenApiSchema Number, OpenApiSchema? NumericString, OpenApiSchema? Named);

    /// <summary>
    /// The branches when <paramref name="schema"/> is the union number handling produces: an
    /// untyped (or, in its 3.0 nullable form, <c>null</c>-typed) <c>anyOf</c> whose first branch is a
    /// number; <see langword="null"/> otherwise.
    /// </summary>
    private static NumberUnion? NumberUnionBranches(OpenApiSchema schema)
    {
        if ((schema.Type.HasValue && schema.Type.Value != JsonSchemaType.Null)
            || schema.AnyOf is not [OpenApiSchema { Type: { } first } number, ..]
            || (first & (JsonSchemaType.Integer | JsonSchemaType.Number)) == 0)
            return null;

        var rest = schema.AnyOf.Skip(1).OfType<OpenApiSchema>().ToList();
        return new NumberUnion(
            number,
            rest.FirstOrDefault(b => b.Type == JsonSchemaType.String && b.Pattern != null),
            rest.FirstOrDefault(b => b.Type == null && b.Enum is { Count: > 0 }));
    }

    /// <summary>
    /// Allowed / denied values on a number-handling union. Allowed values constrain the numeric branch
    /// only (the numeric-string branch keeps its grammar: STJ also reads "+1" and "01") and drop the
    /// named-literal branch, since they are finite numbers. Denied values exclude the numbers on the
    /// numeric branch and their written string forms on the numeric-string branch. Values are checked
    /// against the numeric branch's type.
    /// </summary>
    private void ApplyValuesToNumberUnion(
        OpenApiSchema schema, NumberUnion union, CustomAttributeData? allowed, CustomAttributeData? denied, ValueSite site)
    {
        var numberType = union.Number.Type!.Value & ~JsonSchemaType.Null;

        static List<JsonNode?> AsStrings(List<JsonNode?> values) =>
            values.Select(v => (JsonNode?)JsonValue.Create(v!.ToJsonString())).ToList();

        if (allowed != null && ConvertValues(allowed, numberType, null, site, "AllowedValues") is { } allowedValues)
        {
            // Only the numeric branch: the numeric-string branch keeps its grammar, since STJ reads
            // other spellings of an allowed number ("+1", "01") and the schema is never narrower.
            union.Number.Enum = allowedValues;
            if (union.Named != null)
                schema.AnyOf!.Remove(union.Named);
        }

        if (denied != null && ConvertValues(denied, numberType, null, site, "DeniedValues") is { } deniedValues)
        {
            union.Number.Not = new OpenApiSchema { Enum = deniedValues };
            if (union.NumericString != null)
                union.NumericString.Not = new OpenApiSchema { Enum = AsStrings(deniedValues) };
        }
    }

    /// <summary>One allowed value: <c>const</c> for a string in 3.1+, otherwise a one-element <c>enum</c>.</summary>
    private OpenApiSchema SingleValueConstraint(JsonNode? value)
    {
        if (_options.OpenApiVersion != OpenApiSpecVersion.OpenApi3_0 && value is JsonValue v && v.TryGetValue<string>(out var text))
            return new OpenApiSchema { Const = text };
        return new OpenApiSchema { Enum = [value] };
    }

    /// <summary>
    /// The attribute's values as JSON values of the schema's type (<paramref name="jsonType"/>; any type
    /// when <see langword="null"/>, a reference wrapper), or <see langword="null"/> with a warning when
    /// one does not convert: numbers stay numbers, enum members become the schema's names or integers.
    /// </summary>
    private List<JsonNode?>? ConvertValues(
        CustomAttributeData attribute, JsonSchemaType? jsonType, Type? enumType, ValueSite site, string attributeName)
    {
        var arguments = attribute.ConstructorArguments.Count == 1
                        && attribute.ConstructorArguments[0].Value is IReadOnlyCollection<CustomAttributeTypedArgument> values
            ? values
            : (IReadOnlyCollection<CustomAttributeTypedArgument>)attribute.ConstructorArguments;

        var result = new List<JsonNode?>();
        foreach (var argument in arguments)
        {
            if (!TryConvertValue(argument, jsonType, enumType, site.EnumNaming, out var node))
            {
                RecordLoss(new PendingLoss
                {
                    Class    = LossClass.Source,
                    Code     = ExtractionDiagnosticCodes.SchemaValueNotConvertible,
                    Anchor   = site.Anchor,
                    Message  = $"[{attributeName}] on {site.TypeName}.{site.MemberName} has the value {argument.Value ?? "null"}, " +
                               "which is not a value of its JSON type: the constraint is not written.",
                    Feature  = "schema.enum",
                    Action   = DiagnosticAction.Omitted,
                    Subjects = [$"{site.TypeName}.{site.MemberName}"],
                });
                return null;
            }

            result.Add(node);
        }

        return result;
    }

    private static readonly HashSet<string> IntegralTypes = new(StringComparer.Ordinal)
    {
        "System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
    };

    private static bool TryConvertValue(
        CustomAttributeTypedArgument argument, JsonSchemaType? jsonType, Type? enumType, EnumWireNaming enumNaming, out JsonNode? node)
    {
        node = null;
        bool Allows(JsonSchemaType t) => jsonType == null || (jsonType.Value & t) != 0;

        var value = argument.Value;
        if (value == null)
            return jsonType == null || Allows(JsonSchemaType.Null);

        if (argument.ArgumentType.IsEnum)
        {
            var field = argument.ArgumentType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(f => Equals(f.GetRawConstantValue(), value));
            if (jsonType.HasValue && (jsonType.Value & JsonSchemaType.String) != 0 && field != null)
            {
                node = JsonValue.Create(EnumWireName(field, enumNaming));
                return true;
            }

            if (Allows(JsonSchemaType.Integer))
            {
                node = IntegralValue(value);
                return true;
            }

            return false;
        }

        switch (value)
        {
            case string text when Allows(JsonSchemaType.String) && enumType == null:
                node = JsonValue.Create(text);
                return true;
            case string text when enumType != null && jsonType.HasValue && (jsonType.Value & JsonSchemaType.String) != 0
                                  && enumType.GetField(text) != null:
                node = JsonValue.Create(text);
                return true;
            case bool flag when Allows(JsonSchemaType.Boolean):
                node = JsonValue.Create(flag);
                return true;
        }

        var typeName = argument.ArgumentType.FullName ?? string.Empty;
        if (IntegralTypes.Contains(typeName) && (Allows(JsonSchemaType.Integer) || Allows(JsonSchemaType.Number)))
        {
            node = IntegralValue(value);
            return true;
        }

        if (typeName is "System.Double" or "System.Single" && Allows(JsonSchemaType.Number))
        {
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number))
                return false;
            node = JsonValue.Create(number);
            return true;
        }

        return false;
    }

    /// <summary>
    /// <c>[DefaultValue]</c> on a property, through the converter shared with action parameters; a
    /// value that does not convert to its type gives a warning and no <c>default</c>.
    /// </summary>
    private void ApplyDefaultValue(
        OpenApiSchema schema, IList<CustomAttributeData> attrData, PropertyInfo property, string serializedName, string componentId)
    {
        var attribute = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.DefaultValue);
        if (attribute == null)
            return;

        // The JSON type of the value: the schema's own, or the numeric branch of a number union.
        var valueType = schema.Type is { } own && own != JsonSchemaType.Null
            ? own
            : NumberUnionBranches(schema)?.Number.Type ?? (schema.AllOf is [OpenApiSchema first, ..] ? first.Type : null);
        var result = DefaultValueConverter.FromAttribute(
            attribute, valueType, PropertyEnumNaming(property.PropertyType, attrData) ?? EnumWireNaming.MemberName);
        if (result.HasValue)
        {
            schema.Default = result.Value;
            return;
        }

        if (result.Error == null)
            return;

        var typeName = property.DeclaringType?.FullName ?? componentId;
        RecordLoss(new PendingLoss
        {
            Class    = LossClass.Source,
            Code     = ExtractionDiagnosticCodes.SchemaDefaultNotConvertible,
            Anchor   = new LossAnchor.Node(new LossAnchor.Component(componentId), ["properties", serializedName]),
            Message  = $"[DefaultValue] on {typeName}.{property.Name}: {result.Error}; no default is written.",
            Feature  = "schema.default",
            Action   = DiagnosticAction.Omitted,
            Subjects = [$"{typeName}.{property.Name}"],
        });
    }

    // =========================================================================
    // XML examples
    // =========================================================================

    /// <summary>
    /// The XML example of a property, parsed by the property's schema and written in the version's
    /// form: on the <c>allOf</c> wrapper of a reference (the shared component is unchanged), on the
    /// numeric branch of a number-handling union. A value that does not parse, or several examples,
    /// give a warning on the property.
    /// </summary>
    private IOpenApiSchema ApplyPropertyExample(
        IOpenApiSchema propSchema, Type declaringType, PropertyInfo property, string serializedName, string componentId)
    {
        if (_docResolver == null)
            return propSchema;

        var doc = _docResolver.ResolveProperty(declaringType, property);
        if (doc.Example == null)
            return propSchema;

        var element = $"{property.DeclaringType?.FullName ?? componentId}.{property.Name}";
        var anchor = new LossAnchor.Node(new LossAnchor.Component(componentId), ["properties", serializedName]);
        var schema = EnsureMutableSchema(propSchema);
        WriteExample(schema, doc.Example, doc.ExampleCount, element, anchor);
        return schema;
    }

    /// <summary>The XML example of a type, on its object component; see <see cref="ApplyPropertyExample"/>.</summary>
    private void ApplyTypeExample(OpenApiSchema schema, Type type, string componentId)
    {
        if (_docResolver?.ResolveTypeExample(type) is not { } example)
            return;

        WriteExample(schema, example.Text, example.Count, type.FullName ?? componentId, new LossAnchor.Component(componentId));
    }

    private void WriteExample(OpenApiSchema schema, string text, int count, string element, LossAnchor anchor)
    {
        if (count > 1)
        {
            RecordLoss(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SchemaExampleMultiple,
                Anchor   = anchor,
                Message  = $"{element} has {count} <example> elements: the first is used.",
                Feature  = "schema.example.multiple",
                Action   = DiagnosticAction.Omitted,
                Subjects = [element],
            });
        }

        var (place, jsonType, nullable) = ExampleTarget(schema);
        if (ParseExample(text, jsonType, nullable) is { } value)
        {
            VersionedSchemaForms.SetExample(text == "null" ? schema : place, value, _options.OpenApiVersion);
            return;
        }

        RecordLoss(new PendingLoss
        {
            Class    = LossClass.Source,
            Code     = ExtractionDiagnosticCodes.SchemaExampleNotParsable,
            Anchor   = anchor,
            Message  = $"The <example> of {element}, \"{text}\", is not a value of its schema: no example is written.",
            Feature  = "schema.example",
            Action   = DiagnosticAction.Omitted,
            Subjects = [element, text],
        });
    }

    /// <summary>
    /// Where an example goes and the JSON type it is parsed by: the numeric branch of a number union,
    /// the component's type behind a reference wrapper (or its nullable <c>anyOf</c>), else the schema
    /// itself. The type is <see langword="null"/> when the schema does not restrict it.
    /// </summary>
    private (OpenApiSchema Place, JsonSchemaType? Type, bool Nullable) ExampleTarget(OpenApiSchema schema)
    {
        static bool HasNull(JsonSchemaType? type) => type.HasValue && (type.Value & JsonSchemaType.Null) != 0;

        var nullBranch = schema.AnyOf?.Any(b => b is OpenApiSchema { Type: JsonSchemaType.Null }) == true;

        if (NumberUnionBranches(schema) is { } union)
            return (union.Number, WithoutNull(union.Number.Type), HasNull(schema.Type) || nullBranch);

        if (schema.AllOf is [OpenApiSchemaReference wrapped, ..])
            return (schema, ComponentType(wrapped), false);

        if (schema.AnyOf is [OpenApiSchemaReference nullableRef, ..] && nullBranch)
            return (schema, ComponentType(nullableRef), true);

        return (schema, WithoutNull(schema.Type), HasNull(schema.Type));
    }

    /// <summary>The JSON type of a schema without its <c>null</c> flag; <see langword="null"/> when it has no other.</summary>
    private static JsonSchemaType? WithoutNull(JsonSchemaType? type) =>
        type.HasValue && (type.Value & ~JsonSchemaType.Null) != 0 ? type.Value & ~JsonSchemaType.Null : null;

    /// <summary>The JSON type of the component a reference points to, if it is one of this generator's.</summary>
    private JsonSchemaType? ComponentType(OpenApiSchemaReference reference) =>
        reference.Reference.Id is { } id && _schemas.TryGetValue(id, out var component) ? WithoutNull(component.Type) : null;

    /// <summary>
    /// Parses an XML example <paramref name="text"/> as a value of <paramref name="schema"/> (a
    /// parameter's or a request body's schema, a reference included) by the rules of property
    /// examples; <see langword="false"/> when it does not parse.
    /// </summary>
    internal bool TryParseExample(IOpenApiSchema schema, string text, out JsonNode? value)
    {
        var (type, nullable) = schema switch
        {
            OpenApiSchemaReference reference => (ComponentType(reference), false),
            OpenApiSchema inline => ExampleTarget(inline) is var target ? (target.Type, target.Nullable) : default,
            _ => (null, false),
        };
        value = ParseExample(text, type, nullable);
        return value != null;
    }

    /// <summary>
    /// The example text as a value of <paramref name="jsonType"/>: a string schema takes the text as
    /// it is; numbers, booleans, objects and arrays are parsed as JSON of that kind (an integer must be
    /// integral); the text <c>null</c> is JSON null for a nullable schema only. <see langword="null"/>
    /// when it does not parse.
    /// </summary>
    private static JsonNode? ParseExample(string text, JsonSchemaType? jsonType, bool nullable)
    {
        if (text == "null")
            return nullable ? JsonNullSentinel.JsonNull : null;

        if (jsonType.HasValue && (jsonType.Value & JsonSchemaType.String) != 0)
            return JsonValue.Create(text);

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (node == null || !jsonType.HasValue)
            return node;

        var kind = node.GetValueKind();
        var type = jsonType.Value;
        bool Is(JsonSchemaType t) => (type & t) != 0;

        return kind switch
        {
            System.Text.Json.JsonValueKind.Number when Is(JsonSchemaType.Number) => node,
            System.Text.Json.JsonValueKind.Number when Is(JsonSchemaType.Integer) && IsIntegral(node.AsValue()) => node,
            System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False when Is(JsonSchemaType.Boolean) => node,
            System.Text.Json.JsonValueKind.Object when Is(JsonSchemaType.Object) => node,
            System.Text.Json.JsonValueKind.Array when Is(JsonSchemaType.Array) => node,
            _ => null,
        };
    }

    private static bool IsIntegral(JsonValue value) =>
        value.TryGetValue<long>(out _)
        || value.TryGetValue<ulong>(out _)
        || (value.TryGetValue<decimal>(out var number) && number == decimal.Truncate(number));

    /// <summary>
    /// <c>[Range]</c> on a numeric property: <c>minimum</c> / <c>maximum</c>, or for an exclusive side
    /// <c>exclusiveMinimum</c> / <c>exclusiveMaximum</c> as a number (the serializer writes the 3.0 form,
    /// bound + boolean flag, without loss). A declaration <c>RangeAttribute</c> rejects is an extraction
    /// error; a non-numeric operand type or a bound JSON cannot hold (infinity, NaN) gives a warning on
    /// the property and no such constraint.
    /// </summary>
    private void ApplyRange(
        OpenApiSchema schema, IList<CustomAttributeData> attrData, string typeName, string memberName, LossAnchor anchor)
    {
        var attribute = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.Range);
        if (attribute == null || RangeDeclaration.Read(attribute) is not { } range)
            return;


        switch (range.Kind)
        {
            case RangeDeclaration.Outcome.Invalid:
                throw new OpenApiExtractionException(
                    $"[Range] on {typeName}.{memberName} is rejected by RangeAttribute: {range.Detail}.",
                    typeName, memberName);

            case RangeDeclaration.Outcome.NonNumericOperand:
                RecordLoss(new PendingLoss
                {
                    Class    = LossClass.Source,
                    Code     = ExtractionDiagnosticCodes.SchemaRangeNotExpressible,
                    Anchor   = anchor,
                    Message  = $"[Range] on {typeName}.{memberName} compares {range.Detail} values, which are not JSON numbers: " +
                               "no minimum or maximum is written.",
                    Feature  = "schema.minimum",
                    Action   = DiagnosticAction.Omitted,
                    Subjects = [$"{typeName}.{memberName}"],
                });
                return;
        }

        if (range.NonFiniteMinimum != null || range.NonFiniteMaximum != null)
        {
            var bounds = string.Join(" and ", new[] { range.NonFiniteMinimum, range.NonFiniteMaximum }.Where(b => b != null));
            RecordLoss(new PendingLoss
            {
                Class    = LossClass.Source,
                Code     = ExtractionDiagnosticCodes.SchemaRangeNotExpressible,
                Anchor   = anchor,
                Message  = $"[Range] on {typeName}.{memberName} has the bound {bounds}, which JSON cannot hold: that bound is not written.",
                Feature  = "schema.minimum",
                Action   = DiagnosticAction.Omitted,
                Subjects = [$"{typeName}.{memberName}"],
            });
        }

        // With number handling the number is the first branch of an anyOf: the range constrains it only.
        if (NumberUnionBranches(schema) is { } union)
            schema = union.Number;

        if (!schema.Type.HasValue
            || (schema.Type.Value & (JsonSchemaType.Integer | JsonSchemaType.Number)) == 0)
            return; // a range constrains only the numeric branch

        if (range.Minimum != null)
        {
            if (range.MinimumIsExclusive) schema.ExclusiveMinimum = range.Minimum;
            else schema.Minimum = range.Minimum;
        }

        if (range.Maximum != null)
        {
            if (range.MaximumIsExclusive) schema.ExclusiveMaximum = range.Maximum;
            else schema.Maximum = range.Maximum;
        }
    }

    /// <summary>
    /// Applies type-level attributes (e.g. <c>[JsonUnmappedMemberHandling]</c>,
    /// <c>[Obsolete]</c>) to the object schema generated for <paramref name="type"/>.
    /// </summary>
    private static void ApplyTypeAttributes(OpenApiSchema schema, Type type)
    {
        // [JsonUnmappedMemberHandling(Disallow)] → additionalProperties: false
        var unmappedHandling = AttributeHelper.GetAttribute(type,
            AttributeHelper.Names.JsonUnmappedMemberHandling);
        if (unmappedHandling != null)
        {
            // JsonUnmappedMemberHandling.Disallow = 1
            var val = AttributeHelper.GetConstructorArgument<int>(unmappedHandling, 0);
            if (val == 1)
                schema.AdditionalPropertiesAllowed = false;
        }

        // [Obsolete] on the DTO class → deprecated: true on the object schema
        if (AttributeHelper.HasAttribute(type, AttributeHelper.Names.Obsolete))
            schema.Deprecated = true;
    }

    // =========================================================================
    // Property collection
    // =========================================================================

    /// <summary>
    /// Collects all public instance properties from <paramref name="type"/> and its base types,
    /// walking up the inheritance chain. Returns a list of (C# name, type, PropertyInfo) tuples.
    /// </summary>
    private static List<(string Name, Type PropertyType, PropertyInfo Info)> CollectProperties(Type type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(string, Type, PropertyInfo)>();

        var current = type;
        while (current != null && current.FullName != "System.Object")
        {
            var ownProps = current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var prop in ownProps)
            {
                // Only include readable properties; skip indexers.
                // Check CanRead first to short-circuit the GetIndexParameters() allocation.
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length > 0) continue;

                // Derived class properties take precedence over base class.
                if (seen.Add(prop.Name))
                    result.Add((prop.Name, prop.PropertyType, prop));
            }

            current = current.BaseType;
        }

        return result;
    }

    // =========================================================================
    // Property skip / name / required helpers
    // =========================================================================

    /// <summary>
    /// Returns <see langword="true"/> when the property should be excluded from the schema.
    /// Currently handles <c>[JsonIgnore(Condition = Always)]</c> (condition value 1).
    /// Accepts pre-fetched attribute data to avoid repeated GetCustomAttributesData() calls.
    /// </summary>
    private static bool ShouldIgnoreProperty(IList<CustomAttributeData> attrData)
    {
        var attr = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.JsonIgnore);
        if (attr == null)
            return false;

        // [JsonIgnore] with no arguments → always ignore
        if (attr.NamedArguments.Count == 0 && attr.ConstructorArguments.Count == 0)
            return true;

        // [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
        const int jsonIgnoreConditionAlways = 1;
        var condition = AttributeHelper.GetNamedArgument<int>(attr, "Condition");
        return condition == jsonIgnoreConditionAlways;
    }

    /// <summary>
    /// Resolves the serialized name for a property.
    /// Uses <c>[JsonPropertyName]</c> if present; otherwise applies the configured
    /// <see cref="SchemaOptions.NamingPolicy"/>.
    /// Accepts pre-fetched attribute data to avoid repeated GetCustomAttributesData() calls.
    /// </summary>
    private string ResolvePropertyName(IList<CustomAttributeData> attrData, string fallback)
    {
        var jsonNameAttr = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.JsonPropertyName);
        if (jsonNameAttr != null)
        {
            var name = AttributeHelper.GetConstructorArgument<string>(jsonNameAttr, 0);
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return ApplyNamingPolicy(fallback, _options.NamingPolicy);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the property must be included in the schema's
    /// <c>required</c> array. Signals considered: <c>[Required]</c>, <c>[JsonRequired]</c>,
    /// the C# 11+ <c>required</c> modifier (<c>RequiredMemberAttribute</c>), and non-nullable
    /// reference types (NRT).
    /// The <c>required</c> modifier is an explicit developer contract and makes the property
    /// required unconditionally, including for value types — diverging from Swashbuckle, which
    /// does not mark value types required. Non-nullable value types without the <c>required</c>
    /// modifier are NOT auto-required (they always have a default value in .NET).
    /// When <see cref="SchemaOptions.DefaultIgnoreCondition"/> is
    /// <see cref="JsonIgnoreCondition.WhenWritingNull"/>, nullable properties without an
    /// explicit required marker are not required (they are omitted when null).
    /// Accepts pre-fetched attribute data to avoid repeated GetCustomAttributesData() calls.
    /// </summary>
    private bool IsPropertyRequired(IList<CustomAttributeData> attrData, Type propType, PropertyInfo prop)
    {
        // C# 11+ `required` modifier emits RequiredMemberAttribute — treat as required unconditionally,
        // including for value types (unlike NRT inference, `required` is an explicit developer signal).
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.RequiredMember))
            return true;

        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Required))
            return true;

        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.JsonRequired))
            return true;

        // Value types (int, bool, enums, structs) are NOT auto-required.
        // Swashbuckle only marks [Required]-annotated or NRT non-nullable reference types.
        if (propType.IsValueType)
        {
            // Nullable<T> value types: if WhenWritingNull, they are not required
            if (IsNullableValueType(propType)
                && _options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull)
            {
                return false;
            }

            return false;
        }

        // Reference types: use NRT nullability analysis.
        var isNullable = IsNullableReferenceProperty(attrData, prop);

        // When DefaultIgnoreCondition == WhenWritingNull, nullable properties are NOT required
        // because they are omitted from serialization when null.
        if (isNullable && _options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull)
            return false;

        return !isNullable;
    }

    // =========================================================================
    // NRT nullability helpers
    // =========================================================================

    private const string NullableAttributeFullName = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttributeFullName = "System.Runtime.CompilerServices.NullableContextAttribute";

    /// <summary>
    /// Determines whether a reference-type property is nullable via NRT annotations.
    /// Returns <see langword="true"/> (nullable) when the NRT byte annotation is 2,
    /// or conservatively when no annotation is present.
    /// Accepts pre-fetched attribute data to avoid repeated GetCustomAttributesData() calls.
    /// </summary>
    private bool IsNullableReferenceProperty(IList<CustomAttributeData> attrData, PropertyInfo prop)
    {
        // NullableAttribute on the property getter return type
        var nullableAttr = AttributeHelper.GetAttribute(attrData, NullableAttributeFullName);

        if (nullableAttr != null && nullableAttr.ConstructorArguments.Count == 1)
        {
            var arg = nullableAttr.ConstructorArguments[0];
            if (arg.Value is byte b)
                return b == 2; // 1 = not-null, 2 = nullable
            if (arg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> bytes && bytes.Count > 0)
            {
                var first = bytes.First();
                if (first.Value is byte fb)
                    return fb == 2;
            }
        }

        // Fallback: NullableContextAttribute on the declaring type, then assembly
        byte? context = GetNullableContext(prop.DeclaringType)
                     ?? GetAssemblyNullableContext(prop.DeclaringType?.Assembly);

        if (context.HasValue)
            return context.Value == 2;

        // No NRT info — conservatively treat as nullable
        return true;
    }

    private byte? GetNullableContext(Type? type)
    {
        if (type == null) return null;
        var key = type.FullName ?? type.Name;
        if (_nullableContextByType.TryGetValue(key, out var cached))
            return cached;

        var attr = type.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == NullableContextAttributeFullName);
        byte? value = null;
        if (attr != null && attr.ConstructorArguments.Count == 1
            && attr.ConstructorArguments[0].Value is byte b)
            value = b;

        _nullableContextByType[key] = value;
        return value;
    }

    private static byte? GetAssemblyNullableContext(Assembly? assembly)
    {
        if (assembly == null) return null;
        var attr = assembly.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == NullableContextAttributeFullName);
        if (attr != null && attr.ConstructorArguments.Count == 1
            && attr.ConstructorArguments[0].Value is byte b)
            return b;
        return null;
    }

    // =========================================================================
    // Schema ID generation
    // =========================================================================

    /// <summary>
    /// Produces a stable schema identifier for use as the key in components/schemas, reserved
    /// in <see cref="SchemaIdRegistry"/> for the type's direct use.
    /// Follows the Swashbuckle convention: generic types produce names like
    /// <c>UserDtoApiResponse</c> (args first, then base name).
    /// For list-like types used as generic arguments, produces <c>UserDtoList</c>.
    /// When another type already holds the name, the full CLR name is used instead
    /// (<c>.</c> and <c>+</c> replaced by <c>_</c>).
    /// </summary>
    private string GetSchemaId(Type type)
    {
        if (!type.IsGenericType)
            return ReserveNonGenericId(type);

        return ReserveOwnId(
            type,
            candidate: GenericIdCandidate(type, fullBaseName: false),
            fallback:  GenericIdCandidate(type, fullBaseName: true));
    }

    /// <summary>
    /// The key of the component a type's own id names in this generator's context: the union for a
    /// polymorphic base (whose component is the union, so references at its uses stay unchanged),
    /// the direct schema otherwise.
    /// </summary>
    private SchemaKey OwnKey(Type type) =>
        UnionOf(type) != null ? SchemaKey.Union(type, _context) : SchemaKey.Direct(type, _context);

    /// <summary>
    /// Reserves the own id of <paramref name="type"/>. In the HTTP context a type the MVC context of
    /// the build also describes gets <c>Http</c> appended to both names, so the two schemas of one
    /// type are told apart; references and the ids derived from this one (variants, base branch)
    /// follow it.
    /// </summary>
    private string ReserveOwnId(Type type, string candidate, string fallback)
    {
        var key = OwnKey(type);
        var suffix = ContextSuffixFor(key);
        return _schemaIds.Reserve(key, candidate + suffix, fallback + suffix);
    }

    /// <summary>
    /// <c>Http</c> when this is the HTTP context and the MVC generator of the build has written a
    /// schema for the same component; empty otherwise.
    /// </summary>
    private string ContextSuffixFor(SchemaKey key)
    {
        if (_context != SchemaContext.Http || _mvcCounterpart == null)
            return string.Empty;

        return _schemaIds.TryGetId(key.In(SchemaContext.Mvc), out var mvcId) && _mvcCounterpart._schemas.ContainsKey(mvcId)
            ? "Http"
            : string.Empty;
    }

    /// <summary>
    /// The part of a generic id that names a non-generic type argument. In the HTTP context an
    /// argument the MVC context has an id for contributes that id, so <c>Page&lt;Item&gt;</c> is
    /// <c>ItemPage</c> in both contexts before the context suffix.
    /// </summary>
    private string ArgumentIdPart(Type argument)
    {
        if (_context == SchemaContext.Http
            && _schemaIds.TryGetId(OwnKey(argument).In(SchemaContext.Mvc), out var mvcId))
            return mvcId;

        return ReserveNonGenericId(argument);
    }

    /// <summary>
    /// A non-generic type's id is its short name, or its full name when another type holds the
    /// short name. It is reserved as soon as it is computed — also when it is only a part of a
    /// generic id — so the first type to ask keeps the short name.
    /// </summary>
    private string ReserveNonGenericId(Type type)
    {
        var typeFullName = type.FullName ?? type.Name;
        return ReserveOwnId(
            type,
            candidate: type.Name,
            fallback:  typeFullName.Replace('.', '_').Replace('+', '_'));
    }

    /// <summary>
    /// Id of the variant of <paramref name="derived"/> in the polymorphic use of the base whose
    /// union is <paramref name="unionId"/>: <c>{D}As{B}</c>, or the derived type's full name
    /// (<c>.</c>/<c>+</c> → <c>_</c>) + <c>As{B}</c> when another component holds that name.
    /// </summary>
    private string ReserveVariantId(Type derived, Type baseType, string unionId)
    {
        var derivedName = derived.IsGenericType ? GenericIdCandidate(derived, fullBaseName: false) : derived.Name;
        var derivedFullName = derived.IsGenericType
            ? GenericIdCandidate(derived, fullBaseName: true)
            : (derived.FullName ?? derived.Name).Replace('.', '_').Replace('+', '_');
        return _schemaIds.Reserve(
            SchemaKey.Variant(derived, baseType, _context),
            candidate: $"{derivedName}As{unionId}",
            fallback:  $"{derivedFullName}As{unionId}");
    }

    /// <summary>
    /// Builds the generic id <c>{Arg1}And{Arg2}{BaseName}</c>, e.g.
    /// <c>ApiResponse&lt;UserDto&gt;</c> → <c>UserDtoApiResponse</c>,
    /// <c>ApiResponse&lt;List&lt;UserDto&gt;&gt;</c> → <c>UserDtoListApiResponse</c>,
    /// <c>PaginatedResult&lt;UserDto, PaginationMeta&gt;</c> → <c>UserDtoAndPaginationMetaPaginatedResult</c>.
    /// With <paramref name="fullBaseName"/> the base name is the generic definition's full name
    /// (<c>.</c> and <c>+</c> replaced by <c>_</c>). A non-generic argument contributes its own id,
    /// which <see cref="ReserveNonGenericId"/> reserves for it (as before: the first type to ask
    /// keeps the short name). A generic argument contributes its candidate name without reserving
    /// it, because only a type that becomes a component may hold an id.
    /// </summary>
    private string GenericIdCandidate(Type type, bool fullBaseName)
    {
        var definitionName = fullBaseName
            ? (type.GetGenericTypeDefinition().FullName ?? type.Name).Replace('.', '_').Replace('+', '_')
            : type.Name;

        // Strip the backtick arity suffix from the open generic name (e.g. "ApiResponse`1" → "ApiResponse").
        var backtickIndex = definitionName.IndexOf('`');
        var baseName = backtickIndex >= 0
            ? definitionName[..backtickIndex]
            : definitionName;

        var args = type.GetGenericArguments();

        // Build arg name portion without LINQ closure allocation.
        var sb = new StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append("And");
            sb.Append(args[i].IsGenericType
                ? GenericIdCandidate(args[i], fullBaseName: false)
                : ArgumentIdPart(args[i]));
        }
        sb.Append(baseName);
        return sb.ToString();
    }

    // =========================================================================
    // Collection detection helpers (MetadataLoadContext-safe interface walking)
    // =========================================================================

    private const string IEnumerableGenericFullName = "System.Collections.Generic.IEnumerable`1";
    private const string IEnumerableFullName = "System.Collections.IEnumerable";

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> implements
    /// <c>IDictionary&lt;TKey,TValue&gt;</c> by walking its implemented interfaces
    /// using FullName comparison.
    /// </summary>
    private static bool ImplementsDictionaryInterface(Type type)
    {
        foreach (var i in type.GetInterfaces())
        {
            if (!i.IsGenericType) continue;
            var defName = i.GetGenericTypeDefinition().FullName ?? string.Empty;
            if (DictionaryGenericDefinitions.Contains(defName)) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> implements
    /// <c>IEnumerable&lt;T&gt;</c> by walking its implemented interfaces.
    /// </summary>
    private static bool ImplementsEnumerableInterface(Type type)
    {
        foreach (var i in type.GetInterfaces())
        {
            if (!i.IsGenericType) continue;
            if ((i.GetGenericTypeDefinition().FullName ?? string.Empty) == IEnumerableGenericFullName)
                return true;
        }
        return false;
    }

    private static readonly HashSet<string> DictionaryInterfaces = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.IDictionary`2",
        "System.Collections.Generic.IReadOnlyDictionary`2",
    };

    /// <summary>
    /// The key and value types of the <c>IDictionary&lt;TKey, TValue&gt;</c> (or
    /// <c>IReadOnlyDictionary&lt;TKey, TValue&gt;</c>) that <paramref name="type"/> is or implements.
    /// </summary>
    private static (Type Key, Type Value)? DictionaryKeyValue(Type type)
    {
        var candidates = type.IsInterface ? type.GetInterfaces().Prepend(type) : type.GetInterfaces();
        foreach (var candidate in candidates)
        {
            if (candidate.IsGenericType && DictionaryInterfaces.Contains(candidate.GetGenericTypeDefinition().FullName ?? string.Empty))
            {
                var args = candidate.GetGenericArguments();
                return (args[0], args[1]);
            }
        }

        return null;
    }

    private const string SignedIntegerKeyPattern = "^[+-]?[0-9]+$";
    private const string UnsignedIntegerKeyPattern = "^\\+?[0-9]+$";

    private static readonly HashSet<string> SignedIntegerKeys = new(StringComparer.Ordinal)
    {
        "System.SByte", "System.Int16", "System.Int32", "System.Int64",
    };

    private static readonly HashSet<string> UnsignedIntegerKeys = new(StringComparer.Ordinal)
    {
        "System.Byte", "System.UInt16", "System.UInt32", "System.UInt64",
    };

    /// <summary>
    /// <c>propertyNames</c> for dictionary keys of <paramref name="keyType"/>, never narrower than what
    /// System.Text.Json 10 writes and accepts (measured): <c>Guid</c> → <c>format: uuid</c> (only the D
    /// format); integers → an optional sign and digits (leading zeros and <c>+</c> are accepted; the
    /// type's range is not clamped); anything else (string, enum, …) → none. Not in 3.0, where the
    /// keyword does not exist (form chosen by version). A key type with a converter of its own or an
    /// unknown global converter may write other names: none, with a warning when the converter is unknown.
    /// </summary>
    private OpenApiSchema? KeySchema(Type keyType)
    {
        if (_options.OpenApiVersion == OpenApiSpecVersion.OpenApi3_0)
            return null;

        var name = keyType.FullName ?? keyType.Name;

        // A converter on the key type decides how its keys are written.
        var attribute = AttributeHelper.GetAttribute(keyType, AttributeHelper.Names.JsonConverter);
        if (attribute is { ConstructorArguments: [{ Value: Type converterType }] })
        {
            var converter = converterType.FullName ?? converterType.Name;
            if (JsonConverterRegistry.TryGet(converter) == null)
                RecordUnknownKeyConverter(name, converter);
            return null;
        }

        var constrained = name == "System.Guid" || SignedIntegerKeys.Contains(name) || UnsignedIntegerKeys.Contains(name);
        if (!constrained)
            return null;

        // An unknown global converter may handle this key type: no constraint narrower than the wire.
        if (_options.GlobalConverterTypeNames.FirstOrDefault(c => JsonConverterRegistry.TryGet(c) == null) is { } global)
        {
            RecordUnknownKeyConverter(name, global);
            return null;
        }

        if (name == "System.Guid")
            return new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" };

        return new OpenApiSchema
        {
            Type    = JsonSchemaType.String,
            Pattern = SignedIntegerKeys.Contains(name) ? SignedIntegerKeyPattern : UnsignedIntegerKeyPattern,
        };
    }

    private void RecordUnknownKeyConverter(string keyType, string converter) => RecordLoss(new PendingLoss
    {
        Class    = LossClass.Source,
        Code     = ExtractionDiagnosticCodes.SchemaUnknownKeyConverter,
        Anchor   = LossAnchor.Document.Instance,
        Message  = $"dictionary keys of type {keyType} may be written by the unknown converter {converter}: " +
                   "no propertyNames constraint is written for them.",
        Feature  = "schema.propertyNames",
        Action   = DiagnosticAction.Omitted,
        Subjects = [keyType, converter],
    });

    /// <summary>Whether <paramref name="schema"/> is the schema of a dictionary (an object of additional values).</summary>
    private static bool IsDictionarySchema(OpenApiSchema schema) =>
        schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Object) && schema.AdditionalProperties != null;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is a non-generic
    /// enumerable (implements <c>System.Collections.IEnumerable</c> but not the generic
    /// <c>IEnumerable&lt;T&gt;</c>), for example <c>System.Collections.ArrayList</c>.
    /// </summary>
    private static bool IsNonGenericEnumerable(Type type)
    {
        if (type.IsArray) return false;
        if (type.IsGenericType) return false;

        foreach (var i in type.GetInterfaces())
        {
            if (i.FullName == IEnumerableFullName) return true;
        }
        return false;
    }

    // =========================================================================
    // Nullable helpers
    // =========================================================================

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> is <c>Nullable&lt;T&gt;</c>.
    /// Uses FullName comparison (MetadataLoadContext safe).
    /// </summary>
    private static bool IsNullableValueType(Type type)
    {
        return type.IsGenericType
            && type.GetGenericTypeDefinition().FullName == NullableGenericFullName;
    }

    /// <summary>
    /// Adds <c>JsonSchemaType.Null</c> to the type flags of <paramref name="schema"/>
    /// (OpenAPI 3.1 style). When the schema is an <see cref="OpenApiSchemaReference"/>
    /// it is wrapped in an allOf+null composite.
    /// </summary>
    internal static IOpenApiSchema MakeNullable(IOpenApiSchema schema)
    {
        if (schema is OpenApiSchema concrete)
        {
            // Truly-any schema ({}) already permits null; don't invent a type.
            // Only add Null to the type flags when a concrete type is already set.
            if (concrete.Type.HasValue)
                concrete.Type = concrete.Type.Value | JsonSchemaType.Null;
            return concrete;
        }

        // $ref schemas cannot carry extra keywords directly in OpenAPI 3.1.
        // Wrap in an anyOf to express nullability: anyOf: [$ref, {type: null}]
        return new OpenApiSchema
        {
            AnyOf = new List<IOpenApiSchema>
            {
                schema,
                new OpenApiSchema { Type = JsonSchemaType.Null },
            },
        };
    }

    /// <summary>
    /// Returns a mutable <see cref="OpenApiSchema"/> that can carry sibling keywords
    /// (description, default, constraints). When <paramref name="schema"/> is already an
    /// <see cref="OpenApiSchema"/> it is returned unchanged. When it is an
    /// <see cref="OpenApiSchemaReference"/>, it is wrapped in an allOf-composite so that
    /// sibling keywords can be attached per OpenAPI spec rules ($ref siblings are forbidden
    /// in OpenAPI 3.0 and allowed but ambiguous in 3.1 — allOf is unambiguous in both).
    /// </summary>
    internal static OpenApiSchema EnsureMutableSchema(IOpenApiSchema schema)
    {
        if (schema is OpenApiSchema inline) return inline;
        if (schema is OpenApiSchemaReference reference)
            return new OpenApiSchema { AllOf = [reference] };
        return new OpenApiSchema();
    }

    /// <summary>
    /// Returns true when <paramref name="attrData"/> contains any attribute that
    /// <see cref="ApplyValidationAttributes"/> would write to a schema as a sibling keyword.
    /// Used to decide whether a <c>$ref</c> schema must be wrapped before applying attrs.
    /// </summary>
    private static bool HasAnySiblingAttribute(IList<CustomAttributeData> attrData)
    {
        return AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.StringLength)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.MaxLength)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.MinLength)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Range)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.AllowedValues)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.DeniedValues)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.RegularExpression)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.EmailAddress)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Url)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Phone)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.DefaultValue)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Obsolete)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Description)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Length)
            || DeclaredFormat(attrData) != null
            || DocumentationResolver.AttributeDescription(attrData) != null
            || WritesAccessOrTitle(attrData);
    }

    /// <summary>Whether the attributes give a <c>title</c>, or a true <c>readOnly</c> / <c>writeOnly</c>.</summary>
    private static bool WritesAccessOrTitle(IList<CustomAttributeData> attrData)
    {
        if (AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.SwaggerSchema) is { } swaggerSchema)
        {
            if (AttributeHelper.GetNamedArgument<string>(swaggerSchema, "Title") is { Length: > 0 }
                || AttributeHelper.GetNamedArgument<bool>(swaggerSchema, "WriteOnly"))
                return true;
            if (AttributeHelper.TryGetNamedArgument(swaggerSchema, "ReadOnly", out bool readOnly))
                return readOnly;
        }

        return AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.ReadOnly) is { } attribute
            && AttributeHelper.GetConstructorArgument<bool>(attribute, 0);
    }

    // =========================================================================
    // NumberHandling schema modification
    // =========================================================================

    /// <summary>The number handling in force: the property's or type's, else the global option.</summary>
    private JsonNumberHandling? EffectiveNumberHandling =>
        _hasNumberHandlingScope ? _numberHandlingScope : _options.NumberHandling;

    /// <summary>The value of <c>[JsonNumberHandling]</c> in <paramref name="attrData"/>, if any.</summary>
    private static JsonNumberHandling? NumberHandlingAttribute(IList<CustomAttributeData> attrData)
    {
        var attribute = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.JsonNumberHandling);
        return attribute is { ConstructorArguments: [{ Value: int flags }] } ? (JsonNumberHandling)flags : null;
    }

    private static readonly HashSet<string> FloatingPointTypes = new(StringComparer.Ordinal)
    {
        "System.Single", "System.Double", "System.Half",
    };

    /// <summary>A numeric string System.Text.Json reads for an integer: optional sign, digits (leading zeros allowed).</summary>
    internal const string IntegerStringPattern = "^[+-]?[0-9]+$";

    /// <summary>
    /// A numeric string System.Text.Json reads for <c>float</c>, <c>double</c>, <c>decimal</c> and <c>Half</c>:
    /// optional sign, digits with an optional fraction (<c>.5</c> and <c>1.</c> included), optional exponent.
    /// </summary>
    internal const string FractionalStringPattern = "^[+-]?([0-9]+\\.?[0-9]*|\\.[0-9]+)([eE][+-]?[0-9]+)?$";

    /// <summary>
    /// A numeric string System.Text.Json reads for <c>Half</c>, which it parses with
    /// <c>NumberStyles.Float | AllowThousands</c> (measured on STJ 10): the fractional grammar plus
    /// surrounding white space and group separators in the integer part.
    /// </summary>
    internal const string HalfStringPattern = "^\\s*[+-]?([0-9][0-9,]*\\.?[0-9]*|\\.[0-9]+)([eE][+-]?[0-9]+)?\\s*$";

    /// <summary>
    /// The schema of a number under <paramref name="handling"/>: the union of what System.Text.Json 10
    /// writes and what it reads. <c>AllowReadingFromString</c> and/or <c>WriteAsString</c> → <c>anyOf</c>
    /// [number, numeric string]; for <c>float</c> / <c>double</c> / <c>Half</c>, any of those two flags or
    /// <c>AllowNamedFloatingPointLiterals</c> adds the branch <c>enum: ["NaN", "Infinity", "-Infinity"]</c>.
    /// Without flags (or <c>Strict</c>) the number schema itself.
    /// </summary>
    private static IOpenApiSchema ApplyNumberHandling(OpenApiSchema number, string typeFullName, JsonNumberHandling? handling)
    {
        if (handling is null or JsonNumberHandling.Strict)
            return number;

        var stringFlags = handling.Value & (JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString);
        var isFloatingPoint = FloatingPointTypes.Contains(typeFullName);
        var named = isFloatingPoint && (stringFlags != 0 || (handling.Value & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0);
        if (stringFlags == 0 && !named)
            return number;

        var branches = new List<IOpenApiSchema> { number };
        if (stringFlags != 0)
        {
            var fractional = isFloatingPoint || typeFullName == "System.Decimal";
            branches.Add(new OpenApiSchema
            {
                Type    = JsonSchemaType.String,
                Pattern = typeFullName == "System.Half" ? HalfStringPattern
                    : fractional ? FractionalStringPattern
                    : IntegerStringPattern,
            });
        }

        if (named)
            branches.Add(new OpenApiSchema { Enum = [JsonValue.Create("NaN"), JsonValue.Create("Infinity"), JsonValue.Create("-Infinity")] });

        return new OpenApiSchema { AnyOf = branches };
    }

    // =========================================================================
    // Utility
    // =========================================================================

    /// <summary>
    /// Applies the given naming policy to a PascalCase C# property name.
    /// When the property has a <c>[JsonPropertyName]</c> attribute, that takes
    /// priority and this method is never called.
    /// </summary>
    internal static string ApplyNamingPolicy(string name, JsonNamingPolicy policy)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return policy switch
        {
            JsonNamingPolicy.Preserve      => name,
            JsonNamingPolicy.CamelCase     => ToCamelCase(name),
            JsonNamingPolicy.SnakeCaseLower=> ToSnakeCase(name, upperCase: false),
            JsonNamingPolicy.SnakeCaseUpper=> ToSnakeCase(name, upperCase: true),
            JsonNamingPolicy.KebabCaseLower=> ToKebabCase(name, upperCase: false),
            JsonNamingPolicy.KebabCaseUpper=> ToKebabCase(name, upperCase: true),
            _                              => name,
        };
    }

    /// <summary>
    /// Converts a PascalCase or camelCase identifier to camelCase.
    /// Only lowercases the first character; the rest of the string is preserved as-is.
    /// This matches the behavior of <c>JsonNamingPolicy.CamelCase</c> in System.Text.Json.
    /// </summary>
    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (char.IsLower(name[0]))
            return name;

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// Converts a PascalCase identifier to snake_case.
    /// Inserts a separator between a lowercase letter followed by an uppercase letter,
    /// and between a run of uppercase letters followed by a lowercase letter (acronyms).
    /// Examples:
    ///   <c>PascalCase</c>    → <c>pascal_case</c>
    ///   <c>XMLHttpRequest</c>→ <c>xml_http_request</c>
    ///   <c>HTTPSEnabled</c>  → <c>https_enabled</c>
    /// </summary>
    private static string ToSnakeCase(string name, bool upperCase)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new System.Text.StringBuilder(name.Length + 4);

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (i > 0 && char.IsUpper(c))
            {
                bool prevLower  = char.IsLower(name[i - 1]);
                bool nextLower  = i + 1 < name.Length && char.IsLower(name[i + 1]);
                bool prevUpper  = char.IsUpper(name[i - 1]);

                // Insert separator before transition: lowercase→upper or UPPER→Lower (acronym boundary)
                if (prevLower || (prevUpper && nextLower))
                    sb.Append('_');
            }

            sb.Append(upperCase ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a PascalCase identifier to kebab-case using the same word-splitting
    /// rules as <see cref="ToSnakeCase"/> but with <c>-</c> as the separator.
    /// </summary>
    private static string ToKebabCase(string name, bool upperCase)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new System.Text.StringBuilder(name.Length + 4);

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (i > 0 && char.IsUpper(c))
            {
                bool prevLower  = char.IsLower(name[i - 1]);
                bool nextLower  = i + 1 < name.Length && char.IsLower(name[i + 1]);
                bool prevUpper  = char.IsUpper(name[i - 1]);

                if (prevLower || (prevUpper && nextLower))
                    sb.Append('-');
            }

            sb.Append(upperCase ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}

/// <summary>
/// Options that control schema generation behavior.
/// </summary>
public sealed class SchemaOptions
{
    /// <summary>
    /// The naming policy to apply to property names.
    /// Defaults to <see cref="JsonNamingPolicy.CamelCase"/> to match the ASP.NET Core default.
    /// </summary>
    public JsonNamingPolicy NamingPolicy { get; init; } = JsonNamingPolicy.CamelCase;

    /// <summary>
    /// Serialize enums as strings (default: <see langword="false"/>).
    /// When <see langword="false"/>, enums are serialized as their underlying integer values.
    /// Per-type <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> overrides this setting.
    /// </summary>
    public bool EnumAsString { get; init; } = false;

    /// <summary>
    /// The naming policy applied to dictionary key names.
    /// When null, falls back to <see cref="NamingPolicy"/>.
    /// </summary>
    public JsonNamingPolicy? DictionaryKeyPolicy { get; init; }

    /// <summary>
    /// Controls when properties are omitted from the serialized output globally.
    /// When <see cref="JsonIgnoreCondition.WhenWritingNull"/>, nullable properties are
    /// excluded from the <c>required</c> array.
    /// </summary>
    public JsonIgnoreCondition? DefaultIgnoreCondition { get; init; }

    /// <summary>
    /// Controls how numbers are read and written globally.
    /// Affects the generated schema type for numeric properties.
    /// </summary>
    public JsonNumberHandling? NumberHandling { get; init; }

    /// <summary>
    /// Globally registered converter type names. Used by the T6 registry to apply
    /// converter-specific schema transformations.
    /// </summary>
    public IReadOnlyList<string> GlobalConverterTypeNames { get; init; } = [];

    /// <summary>
    /// The enum naming policy of each global converter, by position: entry <c>i</c> belongs to
    /// <see cref="GlobalConverterTypeNames"/>[<c>i</c>]; <see langword="null"/> (or a missing entry)
    /// means none. It is applied to the member names a string-enum converter writes, after the member
    /// attribute the converter reads (<c>new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)</c>).
    /// </summary>
    public IReadOnlyList<JsonNamingPolicy?> GlobalConverterEnumNamingPolicies { get; init; } = [];

    /// <summary>
    /// When <see langword="true"/> (default), the generator builds a markdown-formatted
    /// <c>description</c> on enum schemas that combines the type-level XML summary with a
    /// bullet list of per-value descriptions sourced from XML <c>&lt;summary&gt;</c> or
    /// <c>[Description]</c> fallback. Requires a <see cref="DocumentationResolver"/> to be
    /// supplied to <see cref="SchemaGenerator"/>. When <see langword="false"/>, the description
    /// is left as-is (old behavior: type-level summary only, applied by the document builder).
    /// </summary>
    public bool EnumAutoDescription { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/> (default), emits a <c>x-enum-varnames</c> extension
    /// parallel to the <c>enum[]</c> array. Array length always matches <c>enum[]</c>.
    /// When <see langword="false"/>, the extension is omitted.
    /// </summary>
    public bool EnumVarnames { get; init; } = true;

    /// <summary>
    /// The OpenAPI version the schemas are generated for: <see cref="OpenApiSpecVersion.OpenApi3_0"/>
    /// (default), <see cref="OpenApiSpecVersion.OpenApi3_1"/> or <see cref="OpenApiSpecVersion.OpenApi3_2"/>.
    /// Schemas generated for one version are serialized only into that version.
    /// Any other value makes the <see cref="SchemaGenerator"/> constructor throw
    /// <see cref="OpenApiConfigurationException"/>.
    /// </summary>
    public OpenApiSpecVersion OpenApiVersion { get; init; } = TargetVersion.Default;

    /// <summary>
    /// Receives the warnings produced by the generator, each delivered once per generator
    /// instance. When <see langword="null"/> (default), warnings are printed to
    /// <c>Console.Error</c> as before.
    /// </summary>
    public Action<ExtractionDiagnostic>? OnDiagnostic { get; init; }
}
