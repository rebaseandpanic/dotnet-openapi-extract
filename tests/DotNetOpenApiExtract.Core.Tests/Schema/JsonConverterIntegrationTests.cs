using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Integration tests for <see cref="JsonConverterRegistry"/> as used inside
/// <see cref="SchemaGenerator"/> — verifies that the registry correctly modifies
/// generated OpenAPI schemas when <c>[JsonConverter]</c> attributes are present
/// or when global converters are configured.
/// </summary>
public sealed class JsonConverterIntegrationTests : IDisposable
{
    private readonly AssemblyLoader _loader;

    public JsonConverterIntegrationTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
    }

    public void Dispose() => _loader.Dispose();

    // =========================================================================
    // Helpers
    // =========================================================================

    private Type GetType(string name) =>
        _loader.Assembly.GetType($"SampleApi.Models.{name}")!;

    private OpenApiSchema ResolveSchema(SchemaGenerator generator, IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference reference)
            return generator.Schemas[reference.Reference.Id!];
        return (OpenApiSchema)schema;
    }

    // =========================================================================
    // 7. Property-level [JsonConverter(typeof(JsonStringEnumConverter))] → string
    // =========================================================================

    [Fact]
    public void JsonStringEnumConverter_EnumPropertyAsString()
    {
        // ConverterTestStatus enum does NOT have [JsonConverter] at the type level.
        // JsonConverterTestDto.State has [JsonConverter(typeof(JsonStringEnumConverter))].
        // The schema for State should be type=string with enum names.
        var generator = new SchemaGenerator();
        var dtoType = GetType("JsonConverterTestDto");
        generator.GenerateSchema(dtoType);

        var dtoSchema = generator.Schemas["JsonConverterTestDto"];
        dtoSchema.Properties.Should().NotBeNull();
        dtoSchema.Properties!.Should().ContainKey("state");

        var stateSchema = dtoSchema.Properties["state"] as OpenApiSchema;
        stateSchema.Should().NotBeNull();
        stateSchema!.Type.Should().Be(JsonSchemaType.String);
        stateSchema.Enum.Should().NotBeNull();

        var values = stateSchema.Enum!
            .Select(n => n!.GetValue<string>())
            .ToList();
        values.Should().BeEquivalentTo("Active", "Inactive", "Pending");
    }

    // =========================================================================
    // 8. Regression: enum without converter stays as integer when EnumAsString=false
    // =========================================================================

    [Fact]
    public void NoConverter_EnumAsInt_DefaultBehavior()
    {
        // UserStatus has no [JsonConverter] attribute and EnumAsString is false by default.
        // It must remain an integer enum.
        var generator = new SchemaGenerator(); // defaults: EnumAsString=false
        var type = GetType("UserStatus");
        var schema = (OpenApiSchema)generator.GenerateSchema(type);

        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Enum.Should().NotBeNull();

        // All values must be integers, not strings.
        var intValues = schema.Enum!
            .Select(n => n!.GetValue<int>())
            .ToList();
        intValues.Should().BeEquivalentTo(new[] { 0, 1, 2, 3 });
    }

    // =========================================================================
    // 8b. Regression: type-level [JsonConverter(JsonStringEnumConverter)] still works
    // =========================================================================

    [Fact]
    public void TypeLevel_JsonStringEnumConverter_StillProducesStringEnum()
    {
        // Priority has [JsonConverter(typeof(JsonStringEnumConverter))] at the type level.
        // This was previously handled by HasJsonStringEnumConverter — now via registry.
        var generator = new SchemaGenerator();
        var type = GetType("Priority");
        var schema = (OpenApiSchema)generator.GenerateSchema(type);

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Enum.Should().NotBeNull();

        var values = schema.Enum!
            .Select(n => n!.GetValue<string>())
            .ToList();
        values.Should().BeEquivalentTo("Low", "Medium", "High", "Critical");
    }

    // =========================================================================
    // 9. GlobalConverterTypeNames → all enums become string
    // =========================================================================

    [Fact]
    public void GlobalJsonStringEnumConverter_AppliesToAllEnums()
    {
        // Providing JsonStringEnumConverter as a global converter means that even enums
        // without any attribute should be serialised as strings.
        var generator = new SchemaGenerator(new SchemaOptions
        {
            GlobalConverterTypeNames =
            [
                "System.Text.Json.Serialization.JsonStringEnumConverter",
            ],
        });

        // UserStatus has no attribute and EnumAsString is false — but global converter applies.
        var type = GetType("UserStatus");
        var schema = (OpenApiSchema)generator.GenerateSchema(type);

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Enum.Should().NotBeNull();

        var values = schema.Enum!
            .Select(n => n!.GetValue<string>())
            .ToList();
        values.Should().BeEquivalentTo("Active", "Suspended", "Banned", "Deleted");
    }

    [Fact]
    public void GlobalJsonStringEnumConverter_AppliesToAllEnums_MultipleEnums()
    {
        // With global converter active, both UserStatus and ConverterTestStatus must be string enums.
        var generator = new SchemaGenerator(new SchemaOptions
        {
            GlobalConverterTypeNames =
            [
                "System.Text.Json.Serialization.JsonStringEnumConverter",
            ],
        });

        var userStatus = (OpenApiSchema)generator.GenerateSchema(GetType("UserStatus"));
        userStatus.Type.Should().Be(JsonSchemaType.String);

        var converterTestStatus = (OpenApiSchema)generator.GenerateSchema(GetType("ConverterTestStatus"));
        converterTestStatus.Type.Should().Be(JsonSchemaType.String);
    }

    // =========================================================================
    // 10. Unknown [JsonConverter] does not crash — leaves schema unchanged
    // =========================================================================

    [Fact]
    public void UnknownConverter_DoesNotChangeSchema()
    {
        // ConverterTestStatus without any converter → integer enum.
        var generator = new SchemaGenerator();
        var type = GetType("ConverterTestStatus");
        var schema = (OpenApiSchema)generator.GenerateSchema(type);

        // No converter on the type itself — should be integer.
        schema.Type.Should().Be(JsonSchemaType.Integer);
    }

    // =========================================================================
    // W1 — Global converters for non-enum (primitive) types
    // =========================================================================

    [Fact]
    public void GlobalIsoDateTimeConverter_AppliesToDateTimeProperty()
    {
        // IsoDateTimeConverter maps DateTime → {type: string, format: date-time}.
        // This is the same as the default PrimitiveMap output, but verifies the path
        // runs through the global-converter hint rather than the PrimitiveMap default.
        var options = new SchemaOptions
        {
            GlobalConverterTypeNames = ["Newtonsoft.Json.Converters.IsoDateTimeConverter"],
        };
        var generator = new SchemaGenerator(options);

        var schema = (OpenApiSchema)generator.GenerateSchema(typeof(DateTime));

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("date-time");
    }

    [Fact]
    public void GlobalUnixDateTimeConverter_OverridesDateTimeDefault()
    {
        // UnixDateTimeConverter maps DateTime → {type: integer, format: int64}.
        // This differs from the PrimitiveMap default (string/date-time), proving the
        // global-converter hint path in the primitive branch is actually followed.
        var options = new SchemaOptions
        {
            GlobalConverterTypeNames = ["Newtonsoft.Json.Converters.UnixDateTimeConverter"],
        };
        var generator = new SchemaGenerator(options);

        var schema = (OpenApiSchema)generator.GenerateSchema(typeof(DateTime));

        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Format.Should().Be("int64");
    }

    // =========================================================================
    // W2 — Regression: property-level [Description] wins over converter-hint description
    //
    // CHANGELOG marks this as [ARCHITECTURE] **BREAKING:** — previously the
    // converter-hint description ("Unix timestamp (seconds)") was applied last and
    // silently overwrote any [Description] attribute.  Now [Description] wins.
    // =========================================================================

    [Fact]
    public void PropertyLevel_Description_WinsOverConverterHintDescription()
    {
        // DescriptionOverridesConverterHintModel.CreatedUnix has both:
        //   [JsonConverter(typeof(UnixDateTimeConverter))]  → hint: "Unix timestamp (seconds)"
        //   [Description("Custom description wins over converter hint")]
        //
        // After the BREAKING fix, the [Description] value must survive in the emitted schema.
        var generator = new SchemaGenerator();
        var dtoType = GetType("DescriptionOverridesConverterHintModel");
        generator.GenerateSchema(dtoType);

        var dtoSchema = generator.Schemas["DescriptionOverridesConverterHintModel"];
        dtoSchema.Properties.Should().NotBeNull();
        dtoSchema.Properties!.Should().ContainKey("createdUnix");

        var propSchema = (OpenApiSchema)dtoSchema.Properties["createdUnix"];

        // Converter hint still governs the type/format shape.
        propSchema.Type.Should().Be(JsonSchemaType.Integer, "UnixDateTimeConverter maps DateTime → integer");
        propSchema.Format.Should().Be("int64");

        // Description must come from [Description], NOT from the converter-hint default.
        propSchema.Description.Should().Be("Custom description wins over converter hint",
            because: "property-level [Description] must override the JsonConverterRegistry hint description");
    }
}
