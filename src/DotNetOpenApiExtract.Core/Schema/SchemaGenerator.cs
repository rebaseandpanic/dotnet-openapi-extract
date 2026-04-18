using System.Reflection;
using System.Text.Json.Nodes;
using DotNetOpenApiExtract.Core.Loading;
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
    private readonly Dictionary<string, string> _schemaTypeMap = new(StringComparer.Ordinal); // schema ID → type FullName for collision detection
    private readonly Dictionary<string, Type> _schemaIdToType = new(StringComparer.Ordinal); // schema ID → original Type
    private readonly HashSet<string> _generating = new(StringComparer.Ordinal); // cycle detection
    private readonly SchemaOptions _options;
    private readonly HashSet<string> _warnedConverters = new(StringComparer.Ordinal); // dedup unknown converter warnings

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
    public SchemaGenerator(SchemaOptions? options = null)
    {
        _options = options ?? new SchemaOptions();
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

        return schema;
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
            // Emit a single warning per unique unknown converter to avoid log spam.
            if (_warnedConverters.Add(converterFullName))
                Console.Error.WriteLine(
                    $"[DotNetOpenApiExtract] Unknown [JsonConverter]: {converterFullName} — schema unchanged.");
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

            // Collect all properties including inherited ones.
            var allProperties = CollectProperties(type);

            var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
            var required = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (propName, propType, propInfo) in allProperties)
            {
                // Cache attribute data once per property to avoid repeated GetCustomAttributesData() calls.
                var propAttrData = propInfo.GetCustomAttributesData();

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

                // Apply validation and documentation attributes to inline schemas.
                // $ref schemas cannot carry extra keywords; constraints are applied only
                // when we have a concrete OpenApiSchema instance.
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

            return new OpenApiSchemaReference(schemaId, null);
        }
        finally
        {
            _generating.Remove(schemaId);
        }
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

        // [Description("text")] → description (fallback; do not override an existing description)
        var desc = AttributeHelper.GetAttribute(attrData, AttributeHelper.Names.Description);
        if (desc != null && string.IsNullOrEmpty(schema.Description))
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
    /// <c>required</c> array. Matches Swashbuckle behavior: only <c>[Required]</c>,
    /// <c>[JsonRequired]</c>, and non-nullable reference types (NRT) are required.
    /// Non-nullable value types (e.g. <c>int</c>, <c>bool</c>) are NOT automatically
    /// required — they always have a default value in .NET and Swashbuckle does not
    /// mark them required either.
    /// When <see cref="SchemaOptions.DefaultIgnoreCondition"/> is
    /// <see cref="JsonIgnoreCondition.WhenWritingNull"/>, nullable properties are
    /// never required (they are omitted when null).
    /// Accepts pre-fetched attribute data to avoid repeated GetCustomAttributesData() calls.
    /// </summary>
    private bool IsPropertyRequired(IList<CustomAttributeData> attrData, Type propType, PropertyInfo prop)
    {
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
    /// Produces a stable schema identifier for use as the key in components/schemas.
    /// Follows the Swashbuckle convention: generic types produce names like
    /// <c>UserDtoApiResponse</c> (args first, then base name).
    /// For list-like types used as generic arguments, produces <c>UserDtoList</c>.
    /// </summary>
    private string GetSchemaId(Type type)
    {
        if (!type.IsGenericType)
        {
            var shortName = type.Name;
            var typeFullName = type.FullName ?? type.Name;

            // Check if a DIFFERENT type already claimed this short name
            if (_schemaTypeMap.TryGetValue(shortName, out var existingFullName)
                && existingFullName != typeFullName)
            {
                return typeFullName.Replace('.', '_').Replace('+', '_');
            }

            _schemaTypeMap[shortName] = typeFullName;
            return shortName;
        }

        // Strip the backtick arity suffix from the open generic name (e.g. "ApiResponse`1" → "ApiResponse").
        var backtickIndex = type.Name.IndexOf('`');
        var baseName = backtickIndex >= 0
            ? type.Name[..backtickIndex]
            : type.Name;

        var args = type.GetGenericArguments();

        // Build arg name portion without LINQ closure allocation.
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append("And");
            sb.Append(GetSchemaId(args[i]));
        }
        sb.Append(baseName);

        // Swashbuckle convention: {Arg1}And{Arg2}{BaseName}
        // e.g. ApiResponse<UserDto> → UserDtoApiResponse
        //      ApiResponse<List<UserDto>> → UserDtoListApiResponse
        //      PaginatedResult<UserDto, PaginationMeta> → UserDtoAndPaginationMetaPaginatedResult
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
            concrete.Type = (concrete.Type ?? JsonSchemaType.String) | JsonSchemaType.Null;
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
}
