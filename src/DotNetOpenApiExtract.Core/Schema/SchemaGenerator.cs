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
    private readonly SchemaIdRegistry _schemaIds = new(); // every component id, unique per (type, context, role)
    private readonly Dictionary<string, Type> _schemaIdToType = new(StringComparer.Ordinal); // schema ID → original Type
    private readonly HashSet<string> _generating = new(StringComparer.Ordinal); // cycle detection
    private readonly SchemaOptions _options;
    private readonly DocumentationResolver? _docResolver;
    private readonly DiagnosticBag _diagnostics; // per-instance deduplication of warnings
    private readonly Dictionary<string, PolymorphismInfo?> _polymorphism = new(StringComparer.Ordinal); // base type → union, cached
    private readonly List<PendingLoss> _pendingLosses = []; // direct generation: delivered at the end of the public call
    private LossLedger? _ledger; // document build: the build's ledger
    private int _generationDepth; // public GenerateSchema nesting, to find the end of the outermost call

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
            ["System.UInt32"]        = (JsonSchemaType.Integer, "int32"),
            ["System.Int64"]         = (JsonSchemaType.Integer, "int64"),
            ["System.UInt64"]        = (JsonSchemaType.Integer, "int64"),

            // Number types
            ["System.Single"]        = (JsonSchemaType.Number, "float"),
            ["System.Double"]        = (JsonSchemaType.Number, "double"),
            ["System.Decimal"]       = (JsonSchemaType.Number, "double"),

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
            ["System.Object"]        = (JsonSchemaType.Object, null),
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
    {
        _options = options ?? new SchemaOptions();
        TargetVersion.EnsureSupported(_options.OpenApiVersion, nameof(SchemaOptions.OpenApiVersion));
        _diagnostics = new DiagnosticBag(_options.OnDiagnostic);
        _docResolver = docResolver;
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

    private IOpenApiSchema GenerateSchemaCore(Type type)
    {
        // --- 1. byte[] → base64 binary string (before the array check below) ---
        if (type.IsArray && type.GetElementType()?.FullName == "System.Byte")
            return new OpenApiSchema { Type = JsonSchemaType.String, Format = "byte" };

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
            var inner = type.GetGenericArguments()[0];
            return MakeNullable(GenerateSchema(inner));
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
        if (PrimitiveMap.TryGetValue(fullName, out var primitive))
        {
            // Check if a globally-registered converter overrides the default primitive schema.
            // This handles converters like IsoDateTimeConverter or UnixDateTimeConverter
            // registered globally via SchemaOptions.GlobalConverterTypeNames.
            // Enum types are excluded here — they are handled via HasApplicableGlobalEnumConverter.
            foreach (var converterFullName in _options.GlobalConverterTypeNames)
            {
                var converterHint = JsonConverterRegistry.TryGet(converterFullName);
                if (converterHint != null && JsonConverterRegistry.AppliesToType(converterHint, isEnum: false, type.FullName))
                    return BuildSchemaFromHint(converterHint, type);
            }

            var schema = new OpenApiSchema { Type = primitive.SchemaType };
            if (primitive.Format != null)
                schema.Format = primitive.Format;

            // Apply global NumberHandling to numeric types only
            if (primitive.SchemaType == JsonSchemaType.Integer
                || primitive.SchemaType == JsonSchemaType.Number)
            {
                return ApplyNumberHandling(schema, _options.NumberHandling);
            }

            return schema;
        }

        // --- 5. Enum ---
        if (type.IsEnum)
            return GenerateEnumSchema(type);

        // --- 6. Generic collections and dictionaries ---
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var genericDefFullName = genericDef.FullName ?? string.Empty;

            // Dictionary
            if (DictionaryGenericDefinitions.Contains(genericDefFullName)
                || ImplementsDictionaryInterface(type))
            {
                var args = type.GetGenericArguments();
                var valueType = args.Length >= 2 ? args[1] : typeof(object);
                return new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    AdditionalProperties = GenerateSchema(valueType),
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
        return GenerateComplexSchema(type);
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
    private IOpenApiSchema GenerateEnumSchema(Type enumType)
    {
        bool asString = _options.EnumAsString
            || GetConverterHintForType(enumType, enumType.GetCustomAttributesData())?.SchemaType == JsonSchemaType.String
            || HasApplicableGlobalEnumConverter();

        var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static);

        OpenApiSchema schema;

        if (asString)
        {
            var enumValues = fields
                .Select(f => (JsonNode)JsonValue.Create(f.Name)!)
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
                .Select(f => (JsonNode)JsonValue.Create(GetEnumFieldIntValue(f))!)
                .ToList();

            schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Enum = enumValues,
            };
        }

        // [Obsolete] on the enum type → deprecated: true
        if (AttributeHelper.HasAttribute(enumType, AttributeHelper.Names.Obsolete))
            schema.Deprecated = true;

        // Collect per-value descriptions and emit extensions + auto-description.
        ApplyEnumExtensions(schema, enumType, fields, asString);

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
        bool asString)
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
            if (asString)
            {
                valueRepresentation = fields[i].Name;
            }
            else
            {
                valueRepresentation = GetEnumFieldIntValue(fields[i]).ToString();
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
    /// Reads the integer value of an enum field using the RawConstantValue metadata.
    /// Falls back to field order index when the metadata is not available.
    /// </summary>
    private static int GetEnumFieldIntValue(FieldInfo field)
    {
        try
        {
            var raw = field.GetRawConstantValue();
            return raw switch
            {
                int i    => i,
                uint u   => (int)u,
                long l   => (int)l,
                ulong ul => (int)ul,
                short s  => s,
                ushort us => us,
                byte b   => b,
                sbyte sb => sb,
                _        => 0,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                        or NotSupportedException
                                        or BadImageFormatException)
        {
            return 0;
        }
    }

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
        if (!JsonConverterRegistry.AppliesToType(hint, targetType.IsEnum, targetType.FullName))
            return null;

        return hint;
    }

    /// <summary>
    /// Returns <see langword="true"/> when at least one globally registered converter
    /// (from <see cref="SchemaOptions.GlobalConverterTypeNames"/>) applies to enum types.
    /// </summary>
    private bool HasApplicableGlobalEnumConverter()
    {
        foreach (var name in _options.GlobalConverterTypeNames)
        {
            var hint = JsonConverterRegistry.TryGet(name);
            if (hint == null) continue;
            if (JsonConverterRegistry.AppliesToType(hint, isEnum: true, targetTypeFullName: null))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds an <see cref="OpenApiSchema"/> from a <see cref="ConverterSchemaHint"/>.
    /// For enum types with a string-type hint, enum field names are preserved as string values.
    /// For other types, a simple schema with the specified type/format/description is returned.
    /// </summary>
    private static IOpenApiSchema BuildSchemaFromHint(ConverterSchemaHint hint, Type targetType)
    {
        // For string enum override: produce enum values as names (string schema with enum).
        if (hint.SchemaType == JsonSchemaType.String && targetType.IsEnum)
        {
            var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Static);
            var enumValues = fields
                .Select(f => (JsonNode)JsonValue.Create(f.Name)!)
                .ToList();

            var enumSchema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = enumValues,
            };
            if (!string.IsNullOrEmpty(hint.Description))
                enumSchema.Description = hint.Description;
            return enumSchema;
        }

        // For all other types (DateTime → IsoDateTimeConverter, etc.): plain scalar schema.
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

            PopulateObjectSchema(type, schema);
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
    private void PopulateObjectSchema(Type type, OpenApiSchema schema)
    {
        // Collect all properties including inherited ones.
        var allProperties = CollectProperties(type);

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

            // Determine the serialized property name.
            var serializedName = ResolvePropertyName(propAttrData, propName);

            // Generate the property schema.
            var propSchema = GenerateSchema(propType);

            // Apply property-level [JsonConverter] override.
            // This handles cases such as [JsonConverter(typeof(JsonStringEnumConverter))]
            // placed on a property whose enum type does not carry the converter itself.
            var propConverterHint = GetConverterHintForType(propType, propAttrData);
            if (propConverterHint != null)
            {
                propSchema = BuildSchemaFromHint(propConverterHint, propType);
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
                ApplyValidationAttributes(inlinePropSchema, propAttrData);

            properties[serializedName] = propSchema;

            // Mark as required if annotated or non-nullable (NRT).
            if (IsPropertyRequired(propAttrData, propType, propInfo))
                required.Add(serializedName);
        }

        schema.Properties = properties.Count > 0 ? properties : null;
        schema.Required = required.Count > 0 ? required : null;

        // [JsonUnmappedMemberHandling(Disallow)] on the type → additionalProperties: false
        ApplyTypeAttributes(schema, type);
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
                        PopulateVariantSchema(variant, derived, polymorphism.PropertyName);
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
        var directId = _schemaIds.Reserve(SchemaKey.Direct(type), $"{GetSchemaId(type)}Direct", $"{fullName}Direct");
        if (_schemas.ContainsKey(directId) || !_generating.Add(directId))
            return new OpenApiSchemaReference(directId, null);

        try
        {
            var schema = new OpenApiSchema { Type = JsonSchemaType.Object };
            _schemas[directId] = schema;
            _schemaIdToType[directId] = type;
            PopulateObjectSchema(type, schema);
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
        var branchId = _schemaIds.Reserve(SchemaKey.BaseDefault(baseType), $"{unionId}Default", $"{fullName}Default");

        if (_schemas.ContainsKey(branchId) || !_generating.Add(branchId))
            return new OpenApiSchemaReference(branchId, null);

        try
        {
            var branch = new OpenApiSchema { Type = JsonSchemaType.Object };
            _schemas[branchId] = branch;
            _schemaIdToType[branchId] = baseType;
            PopulateObjectSchema(baseType, branch);

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
    private void PopulateVariantSchema(OpenApiSchema variant, DerivedTypeInfo derived, string propertyName)
    {
        PopulateObjectSchema(derived.Type, variant);

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
    /// unwrapped by the caller before calling this method.
    /// </summary>
    private static void ApplyValidationAttributes(OpenApiSchema schema, IList<CustomAttributeData> attrData)
    {
        // [StringLength(maxLength, MinimumLength = minLength)]
        var stringLength = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.StringLength);
        if (stringLength != null)
        {
            var maxLen = AttributeHelper.GetConstructorArgument<int>(stringLength, 0);
            if (maxLen > 0) schema.MaxLength = maxLen;
            var minLen = AttributeHelper.GetNamedArgument<int>(stringLength, "MinimumLength");
            if (minLen > 0) schema.MinLength = minLen;
        }

        // [MaxLength(n)] → maxLength (strings) or maxItems (arrays)
        var maxLength = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.MaxLength);
        if (maxLength != null)
        {
            var n = AttributeHelper.GetConstructorArgument<int>(maxLength, 0);
            if (n > 0)
            {
                if (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Array))
                    schema.MaxItems = n;
                else
                    schema.MaxLength = n;
            }
        }

        // [MinLength(n)] → minLength (strings) or minItems (arrays)
        var minLength = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.MinLength);
        if (minLength != null)
        {
            var n = AttributeHelper.GetConstructorArgument<int>(minLength, 0);
            if (n > 0)
            {
                if (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Array))
                    schema.MinItems = n;
                else
                    schema.MinLength = n;
            }
        }

        // [Range(min, max)] — constructor overloads: (int,int), (double,double), (Type,string,string)
        var range = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.Range);
        if (range != null && range.ConstructorArguments.Count >= 2)
        {
            var minVal = ConvertToString(range.ConstructorArguments[0].Value);
            var maxVal = ConvertToString(range.ConstructorArguments[1].Value);
            if (minVal != null) schema.Minimum = minVal;
            if (maxVal != null) schema.Maximum = maxVal;
        }

        // [RegularExpression(@"pattern")]
        var regex = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.RegularExpression);
        if (regex != null)
        {
            var pattern = AttributeHelper.GetConstructorArgument<string>(regex, 0);
            if (!string.IsNullOrEmpty(pattern)) schema.Pattern = pattern;
        }

        // [EmailAddress] → format: "email" (only when no format is already set)
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.EmailAddress))
        {
            if (string.IsNullOrEmpty(schema.Format))
                schema.Format = "email";
        }

        // [Url] → format: "uri"
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Url))
        {
            if (string.IsNullOrEmpty(schema.Format))
                schema.Format = "uri";
        }

        // [Phone] → format: "phone"
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Phone))
        {
            if (string.IsNullOrEmpty(schema.Format))
                schema.Format = "phone";
        }

        // [DefaultValue(value)]
        var defaultVal = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.DefaultValue);
        if (defaultVal != null)
        {
            var value = AttributeHelper.GetConstructorArgument<object>(defaultVal, 0);
            if (value != null)
            {
                schema.Default = value switch
                {
                    bool b   => JsonValue.Create(b),
                    int i    => JsonValue.Create(i),
                    long l   => JsonValue.Create(l),
                    float f  => JsonValue.Create(f),
                    double d => JsonValue.Create(d),
                    string s => JsonValue.Create(s),
                    _        => JsonValue.Create(value.ToString()),
                };
            }
        }

        // [Obsolete] → deprecated: true
        if (AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Obsolete))
            schema.Deprecated = true;

        // [Description("text")] → description (always wins; overrides any default set by a converter
        // hint or the BCL registry, because a property-level annotation is a direct user statement).
        var desc = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.Description);
        if (desc != null)
        {
            var text = AttributeHelper.GetConstructorArgument<string>(desc, 0);
            if (!string.IsNullOrEmpty(text)) schema.Description = text;
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

    /// <summary>
    /// Converts a Range constructor argument value to its string representation for use
    /// in OpenAPI <c>minimum</c> / <c>maximum</c> keywords (which are strings in v3.5.0).
    /// Returns <see langword="null"/> when the value cannot be converted to a numeric string.
    /// </summary>
    private static string? ConvertToString(object? value)
    {
        return value switch
        {
            int i       => i.ToString(),
            uint u      => u.ToString(),
            long l      => l.ToString(),
            ulong ul    => ul.ToString(),
            short s     => s.ToString(),
            ushort us   => us.ToString(),
            byte b      => b.ToString(),
            sbyte sb    => sb.ToString(),
            double d    => d.ToString("G"),
            float f     => f.ToString("G"),
            decimal dec => dec.ToString("G"),
            string str  => str,   // Type-based Range("0", "150") passes strings
            null        => null,
            _           => null,
        };
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

        return _schemaIds.Reserve(
            OwnKey(type),
            candidate: GenericIdCandidate(type, fullBaseName: false),
            fallback:  GenericIdCandidate(type, fullBaseName: true));
    }

    /// <summary>
    /// The key of the component a type's own id names: the union for a polymorphic base (whose
    /// component is the union, so references at its uses stay unchanged), the direct schema otherwise.
    /// </summary>
    private SchemaKey OwnKey(Type type) =>
        UnionOf(type) != null ? SchemaKey.Union(type) : SchemaKey.Direct(type);

    /// <summary>
    /// A non-generic type's id is its short name, or its full name when another type holds the
    /// short name. It is reserved as soon as it is computed — also when it is only a part of a
    /// generic id — so the first type to ask keeps the short name.
    /// </summary>
    private string ReserveNonGenericId(Type type)
    {
        var typeFullName = type.FullName ?? type.Name;
        return _schemaIds.Reserve(
            OwnKey(type),
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
            SchemaKey.Variant(derived, baseType),
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
                : ReserveNonGenericId(args[i]));
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
    private static IOpenApiSchema MakeNullable(IOpenApiSchema schema)
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
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.RegularExpression)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.EmailAddress)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Url)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Phone)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.DefaultValue)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Obsolete)
            || AttributeHelper.HasAttribute(attrData, AttributeHelper.Names.Description);
    }

    // =========================================================================
    // NumberHandling schema modification
    // =========================================================================

    /// <summary>
    /// Wraps a numeric schema to reflect global <see cref="JsonNumberHandling"/> settings.
    /// <list type="bullet">
    ///   <item>
    ///     <see cref="JsonNumberHandling.WriteAsString"/> — the schema type becomes <c>string</c>
    ///     with the original format preserved.
    ///   </item>
    ///   <item>
    ///     <see cref="JsonNumberHandling.AllowReadingFromString"/> — wraps in
    ///     <c>anyOf: [{original}, {type: string, pattern: "^-?\\d+(\\.\\d+)?$"}]</c>
    ///     to express that the field can be read from either a number or a string.
    ///     This shape is valid for both OpenAPI 3.0 and 3.1.
    ///   </item>
    /// </list>
    /// Returns the schema unchanged when <paramref name="handling"/> is
    /// <see cref="JsonNumberHandling.Strict"/> (0) or null.
    /// </summary>
    /// <remarks>
    /// When both <c>WriteAsString</c> and <c>AllowReadingFromString</c> are set simultaneously,
    /// <c>WriteAsString</c> takes precedence: the schema becomes <c>{type: string, format: &lt;original&gt;}</c>.
    /// Rationale: the wire format is string in both directions, so no <c>anyOf</c> union is needed.
    /// </remarks>
    private static IOpenApiSchema ApplyNumberHandling(IOpenApiSchema schema, JsonNumberHandling? handling)
    {
        if (handling == null || handling.Value == JsonNumberHandling.Strict)
            return schema;

        if (schema is not OpenApiSchema concrete)
            return schema; // cannot modify $ref schemas inline

        // WriteAsString: the number is written (and read) as a JSON string.
        if ((handling.Value & JsonNumberHandling.WriteAsString) != 0)
        {
            var stringSchema = new OpenApiSchema
            {
                Type   = JsonSchemaType.String,
                Format = concrete.Format,
            };
            return stringSchema;
        }

        // AllowReadingFromString: the number can be read from either a JSON number or a JSON string.
        // We express this as anyOf: [{number schema}, {string + pattern}]
        // which is compatible with both OpenAPI 3.0 and 3.1.
        if ((handling.Value & JsonNumberHandling.AllowReadingFromString) != 0)
        {
            return new OpenApiSchema
            {
                AnyOf = new List<IOpenApiSchema>
                {
                    concrete,
                    new OpenApiSchema
                    {
                        Type    = JsonSchemaType.String,
                        Pattern = @"^-?\d+(\.\d+)?$",
                    },
                },
            };
        }

        return schema;
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
