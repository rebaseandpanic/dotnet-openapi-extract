using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Integration tests for BCL JSON container type handling in <see cref="SchemaGenerator"/>.
/// Verifies that well-known types like <c>JsonElement</c>, <c>JObject</c>, <c>ExpandoObject</c>,
/// etc. are emitted as inline schemas and never registered in <c>components/schemas</c>.
/// </summary>
public sealed class BclJsonSchemaGeneratorTests : IDisposable
{
    private readonly AssemblyLoader _loader;
    private readonly SchemaGenerator _generator;

    public BclJsonSchemaGeneratorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        _generator = new SchemaGenerator();
    }

    public void Dispose() => _loader.Dispose();

    // =========================================================================
    // Helpers
    // =========================================================================

    private Type GetType(string name) =>
        _loader.Assembly.GetType($"SampleApi.Models.{name}")!;

    private OpenApiSchema ResolveBclJsonDto()
    {
        var type = GetType("BclJsonDto");
        _generator.GenerateSchema(type);
        return _generator.Schemas["BclJsonDto"];
    }

    private IOpenApiSchema GetProperty(OpenApiSchema schema, string propertyName) =>
        schema.Properties![propertyName];

    // =========================================================================
    // Non-nullable struct: JsonElement (not nullable by default)
    // =========================================================================

    [Fact]
    public void JsonElement_IsInlineAnySchema_WithCorrectDescription()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "element");

        // JsonElement is a struct — not nullable by default, no MakeNullable applied.
        prop.Type.Should().BeNull("JsonElement must emit as truly-any schema with no type");
        prop.Properties.Should().BeNullOrEmpty();
        prop.Items.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    // =========================================================================
    // Nullable reference types: truly-any shape (Type = null → MakeNullable is a no-op on type)
    // =========================================================================

    [Fact]
    public void JsonNode_NullableRef_IsInlineAnySchemaWithNoTypeSet()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "node");

        // JsonNode? is a nullable reference type. MakeNullable is called on a truly-any schema
        // (Type == null). The MakeNullable fix must NOT add JsonSchemaType.String in this case.
        prop.Type.Should().BeNull("MakeNullable must not invent a type for a truly-any schema");
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    [Fact]
    public void JsonDocument_NullableRef_IsInlineAnySchemaWithNoTypeSet()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "document");

        prop.Type.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    [Fact]
    public void JsonValue_NullableRef_IsInlineAnySchemaWithNoTypeSet()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "value");

        prop.Type.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    [Fact]
    public void JToken_NullableRef_IsInlineAnySchemaWithNoTypeSet()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "token");

        prop.Type.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    [Fact]
    public void JValue_NullableRef_IsInlineAnySchemaWithNoTypeSet()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "jValue");

        prop.Type.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    [Fact]
    public void JRaw_NullableRef_IsInlineAnySchemaWithNoTypeSet()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "jRaw");

        prop.Type.Should().BeNull("JRaw must emit as truly-any schema with no type");
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    // =========================================================================
    // Nullable reference types: Object shape (Type = Object | Null from MakeNullable)
    // =========================================================================

    [Fact]
    public void JsonObject_NullableRef_IsObjectNullableWithAdditionalPropertiesAny()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "object");

        // JsonObject? is a nullable reference type, so MakeNullable adds Null to the type.
        prop.Type.Should().Be(JsonSchemaType.Object | JsonSchemaType.Null);
        prop.AdditionalProperties.Should().NotBeNull();
        var addlProps = (OpenApiSchema)prop.AdditionalProperties!;
        addlProps.Type.Should().BeNull("additionalProperties must be truly-any {}");
        prop.Properties.Should().BeNullOrEmpty();
        prop.Description.Should().Be("Arbitrary JSON object");
    }

    [Fact]
    public void JObject_NullableRef_IsObjectNullableWithAdditionalPropertiesAny()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "jObject");

        prop.Type.Should().Be(JsonSchemaType.Object | JsonSchemaType.Null);
        prop.AdditionalProperties.Should().NotBeNull();
        var addlProps = (OpenApiSchema)prop.AdditionalProperties!;
        addlProps.Type.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON object");
    }

    [Fact]
    public void ExpandoObject_NullableRef_IsObjectNullableWithAdditionalPropertiesAny()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "expando");

        prop.Type.Should().Be(JsonSchemaType.Object | JsonSchemaType.Null);
        prop.AdditionalProperties.Should().NotBeNull();
        var addlProps = (OpenApiSchema)prop.AdditionalProperties!;
        addlProps.Type.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON object");
    }

    // =========================================================================
    // Nullable reference types: Array shape (Type = Array | Null from MakeNullable)
    // =========================================================================

    [Fact]
    public void JsonArray_NullableRef_IsArrayNullableWithItemsAny()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "array");

        // JsonArray? is a nullable reference type, so MakeNullable adds Null to the type.
        prop.Type.Should().Be(JsonSchemaType.Array | JsonSchemaType.Null);
        prop.Items.Should().NotBeNull();
        var items = (OpenApiSchema)prop.Items!;
        items.Type.Should().BeNull("items must be truly-any {}");
        prop.Description.Should().Be("Arbitrary JSON array");
    }

    [Fact]
    public void JArray_NullableRef_IsArrayNullableWithItemsAny()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "jArray");

        prop.Type.Should().Be(JsonSchemaType.Array | JsonSchemaType.Null);
        prop.Items.Should().NotBeNull();
        var items = (OpenApiSchema)prop.Items!;
        items.Type.Should().BeNull();
        prop.Description.Should().Be("Arbitrary JSON array");
    }

    // =========================================================================
    // Nullable<JsonElement> — MakeNullable fix (value type unwrap path)
    // =========================================================================

    [Fact]
    public void NullableJsonElement_DoesNotGetStringType()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "nullableJsonElement");

        // Nullable<JsonElement> hits the Nullable<T> unwrap branch, then MakeNullable is called.
        // The registry returns {type: null}, so MakeNullable must NOT set Type = String|Null.
        prop.Type.Should().BeNull("MakeNullable on a typeless schema must leave Type unset");
        prop.Description.Should().Be("Arbitrary JSON value");
    }

    // =========================================================================
    // Collection wrappers — nullable arrays/lists of JsonElement
    // =========================================================================

    [Fact]
    public void ArrayOfJsonElement_IsNullableOuterArrayWithTrulyAnyItems()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "arrayOfJsonElement");

        // JsonElement[]? is a nullable reference type → Array | Null
        prop.Type.Should().Be(JsonSchemaType.Array | JsonSchemaType.Null);
        prop.Items.Should().NotBeNull();
        var items = (OpenApiSchema)prop.Items!;
        items.Type.Should().BeNull("items should be the BCL registry truly-any schema");
        items.Description.Should().Be("Arbitrary JSON value");
    }

    [Fact]
    public void ListOfJsonElement_IsNullableOuterArrayWithTrulyAnyItems()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "listOfJsonElement");

        // List<JsonElement>? is a nullable reference type → Array | Null
        prop.Type.Should().Be(JsonSchemaType.Array | JsonSchemaType.Null);
        prop.Items.Should().NotBeNull();
        var items = (OpenApiSchema)prop.Items!;
        items.Type.Should().BeNull();
        items.Description.Should().Be("Arbitrary JSON value");
    }

    // =========================================================================
    // Dictionary wrappers — negative and positive cases
    // =========================================================================

    [Fact]
    public void DictionaryOfJsonElement_HasTrulyAnyAdditionalProperties()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "dictionaryOfJsonElement");

        // Dictionary<string, JsonElement>? → Object | Null (nullable reference type)
        prop.Type.Should().Be(JsonSchemaType.Object | JsonSchemaType.Null);
        prop.AdditionalProperties.Should().NotBeNull();
        var addlProps = (OpenApiSchema)prop.AdditionalProperties!;
        addlProps.Type.Should().BeNull("additionalProperties for JsonElement must be truly-any");
        addlProps.Description.Should().Be("Arbitrary JSON value");
    }

    [Fact]
    public void DictionaryOfString_AdditionalPropertiesIsStringSchema()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "dictionaryOfString");

        // Dictionary<string, string>? → Object | Null (nullable reference type)
        prop.Type.Should().Be(JsonSchemaType.Object | JsonSchemaType.Null);
        prop.AdditionalProperties.Should().NotBeNull();
        var addlProps = (OpenApiSchema)prop.AdditionalProperties!;
        addlProps.Type.Should().Be(JsonSchemaType.String, "negative case: string dictionary is unchanged");
    }

    [Fact]
    public void DictionaryOfTypedDto_AdditionalPropertiesIsRef()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "dictionaryOfTypedDto");

        // Dictionary<string, TypedDto>? → Object | Null (nullable reference type)
        prop.Type.Should().Be(JsonSchemaType.Object | JsonSchemaType.Null);
        prop.AdditionalProperties.Should().BeOfType<OpenApiSchemaReference>(
            "TypedDto is a complex type — should use $ref, not inline BCL schema");
    }

    // =========================================================================
    // [Description] override wins over registry default
    // =========================================================================

    [Fact]
    public void NullableBcl_WithDescription_PreservesUserDescriptionAndDoesNotInventStringType()
    {
        // Exercises the three-way composition: BCL registry → ApplyValidationAttributes
        // (user [Description] wins over registry default) → MakeNullable (must NOT add a type
        // when the base schema is truly-any / Type == null).
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "tenantConfig");

        // 1. User [Description] survives — overrides registry default "Arbitrary JSON value".
        prop.Description.Should().Be("Tenant-specific config blob",
            because: "property-level [Description] must win over the BCL registry default");

        // 2. MakeNullable must NOT invent a type: Type remains unset (null) even after
        //    making the schema nullable, because the base BCL Any shape has no type.
        prop.Type.Should().BeNull(
            because: "MakeNullable on a truly-any schema must not add JsonSchemaType.String or any other type");

        // 3. Schema structure is still truly-any — no properties, items, or additionalProperties.
        prop.Properties.Should().BeNullOrEmpty();
        prop.Items.Should().BeNull();
        prop.AdditionalProperties.Should().BeNull();
    }

    [Fact]
    public void MetadataWithDescription_UserDescriptionWinsOverRegistryDefault()
    {
        var schema = ResolveBclJsonDto();
        var prop = (OpenApiSchema)GetProperty(schema, "metadataWithDescription");

        // JsonElement (not nullable struct) — still truly-any schema shape.
        prop.Type.Should().BeNull("still truly-any schema shape");
        prop.Description.Should().Be("Request metadata as free-form JSON",
            because: "property-level [Description] must override the BCL registry default");
    }

    // =========================================================================
    // No BCL types leak into components/schemas
    // =========================================================================

    [Fact]
    public void BclJsonTypes_AreNotRegisteredInComponentsSchemas()
    {
        ResolveBclJsonDto(); // trigger full generation

        var bcl = new[]
        {
            "JsonElement", "JsonNode", "JsonObject", "JsonArray", "JsonValue",
            "JsonDocument", "JToken", "JObject", "JArray", "JValue", "JRaw", "ExpandoObject",
        };

        foreach (var name in bcl)
        {
            _generator.Schemas.Should().NotContainKey(name,
                because: $"BCL JSON type '{name}' must be emitted inline, not registered in components/schemas");
        }

        // Positive assertion: BclJsonDto itself must be registered.
        _generator.Schemas.Should().ContainKey("BclJsonDto");

        // TypedDto from the negative dictionary case must also be registered.
        _generator.Schemas.Should().ContainKey("TypedDto");
    }

    // =========================================================================
    // I2 — Wire-format serialization snapshot
    // Verifies that the in-memory schema shapes produced by the generator
    // actually serialize to the expected on-the-wire JSON via Microsoft.OpenApi 3.5.0.
    // =========================================================================

    [Fact]
    public async Task BclJsonSchemas_SerializeToExpectedJsonWireFormat()
    {
        var schema = ResolveBclJsonDto();

        // Pull the three representative property schemas.
        // element  → JsonElement (truly-any: Type == null)
        // object   → JsonObject  (object + additionalProperties: {})
        // array    → JsonArray   (array  + items: {})
        var elementPropSchema = (OpenApiSchema)GetProperty(schema, "element");
        var objectPropSchema  = (OpenApiSchema)GetProperty(schema, "object");
        var arrayPropSchema   = (OpenApiSchema)GetProperty(schema, "array");

        // Serialize each schema to JSON using the Microsoft.OpenApi async helper.
        // Observed output with Microsoft.OpenApi 3.5.0:
        //   any-schema  → {"description":"Arbitrary JSON value"}          — no "type" key at all
        //   object-schema → {"type":"object","additionalProperties":{},"description":"..."}
        //   array-schema  → {"type":"array","items":{},"description":"..."}
        var ct = TestContext.Current.CancellationToken;
        var elementJson = await elementPropSchema.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, ct);
        var objectJson  = await objectPropSchema.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, ct);
        var arrayJson   = await arrayPropSchema.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, ct);

        // JsonElement (truly-any) — description present, no "type" key in JSON output.
        // NOTE: Microsoft.OpenApi 3.5.0 omits the "type" field entirely when Type == null
        // (it does NOT write "type": null). This is the correct wire behaviour.
        elementJson.Should().Contain("\"description\": \"Arbitrary JSON value\"");
        elementJson.Should().NotContain("\"type\"",
            because: "a truly-any schema must not emit a type field on the wire");

        // JsonObject — type=object with additionalProperties: {}
        // The nullable flag is set (JsonObject? property), so the outer schema has
        // Type = Object | Null. In OpenAPI 3.0 serialization, Object|Null emits as
        // "type": "object" with a separate "nullable": true field.
        // NOTE: Microsoft.OpenApi 3.5.0 writes keys with ": " (space after colon) and
        // empty schemas as "{ }" (space inside braces), matching the observed wire format.
        objectJson.Should().Contain("\"type\": \"object\"",
            because: "JsonObject shape must emit type:object specifically, not type:array");
        objectJson.Should().Contain("\"additionalProperties\": { }");
        objectJson.Should().Contain("\"description\": \"Arbitrary JSON object\"");

        // JsonArray — type=array with items: {}
        // Type = Array | Null emits as "type": "array" with "nullable": true in OpenAPI 3.0.
        arrayJson.Should().Contain("\"type\": \"array\"",
            because: "JsonArray shape must emit type:array specifically, not type:object");
        arrayJson.Should().Contain("\"items\": { }");
        arrayJson.Should().Contain("\"description\": \"Arbitrary JSON array\"");
    }
}
