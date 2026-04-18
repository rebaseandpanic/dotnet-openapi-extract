using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Comprehensive unit tests for <see cref="SchemaGenerator"/> covering primitive types,
/// nullable types, enums, collections, complex types with $ref, inheritance, self-referencing
/// types, JSON attributes, validation attributes, and schema options.
/// </summary>
public sealed class SchemaGeneratorTests : IDisposable
{
    private readonly AssemblyLoader _loader;
    private readonly SchemaGenerator _generator;

    public SchemaGeneratorTests()
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

    /// <summary>
    /// Gets a property schema by property name from a resolved object schema.
    /// </summary>
    private IOpenApiSchema GetProperty(OpenApiSchema schema, string propertyName) =>
        schema.Properties![propertyName];

    /// <summary>
    /// Resolves a potentially-referenced schema to its concrete <see cref="OpenApiSchema"/>.
    /// If the input is an <see cref="OpenApiSchemaReference"/>, looks it up in the generator's
    /// <see cref="SchemaGenerator.Schemas"/> dictionary.
    /// </summary>
    private OpenApiSchema ResolveSchema(IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference reference)
            return _generator.Schemas[reference.Reference.Id!];
        return (OpenApiSchema)schema;
    }

    // =========================================================================
    // Primitives — AllPrimitivesModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_String_ReturnsStringTypeWithNoFormat()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "stringProp");

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().BeNull();
    }

    [Fact]
    public void GenerateSchema_Bool_ReturnsBooleanType()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "boolProp");

        schema.Type.Should().Be(JsonSchemaType.Boolean);
    }

    [Fact]
    public void GenerateSchema_Int_ReturnsIntegerTypeWithInt32Format()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "intProp");

        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Format.Should().Be("int32");
    }

    [Fact]
    public void GenerateSchema_Long_ReturnsIntegerTypeWithInt64Format()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "longProp");

        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Format.Should().Be("int64");
    }

    [Fact]
    public void GenerateSchema_Float_ReturnsNumberTypeWithFloatFormat()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "floatProp");

        schema.Type.Should().Be(JsonSchemaType.Number);
        schema.Format.Should().Be("float");
    }

    [Fact]
    public void GenerateSchema_Double_ReturnsNumberTypeWithDoubleFormat()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "doubleProp");

        schema.Type.Should().Be(JsonSchemaType.Number);
        schema.Format.Should().Be("double");
    }

    [Fact]
    public void GenerateSchema_Decimal_ReturnsNumberTypeWithDoubleFormat()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "decimalProp");

        schema.Type.Should().Be(JsonSchemaType.Number);
        schema.Format.Should().Be("double");
    }

    [Fact]
    public void GenerateSchema_DateTime_ReturnsStringTypeWithDateTimeFormat()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "dateTimeProp");

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("date-time");
    }

    [Fact]
    public void GenerateSchema_Guid_ReturnsStringTypeWithUuidFormat()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "guidProp");

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("uuid");
    }

    [Fact]
    public void GenerateSchema_DateOnly_ReturnsStringTypeWithDateFormat()
    {
        var type = GetType("AllPrimitivesModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "dateOnlyProp");

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("date");
    }

    // =========================================================================
    // Nullable types — NullableModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_NullableInt_ReturnsIntegerAndNullFlags()
    {
        var type = GetType("NullableModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "nullableInt");

        schema.Type.Should().NotBeNull();
        schema.Type!.Value.HasFlag(JsonSchemaType.Integer).Should().BeTrue();
        schema.Type!.Value.HasFlag(JsonSchemaType.Null).Should().BeTrue();
    }

    [Fact]
    public void GenerateSchema_NullableGuid_ReturnsStringAndNullFlags()
    {
        var type = GetType("NullableModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "nullableGuid");

        schema.Type.Should().NotBeNull();
        schema.Type!.Value.HasFlag(JsonSchemaType.String).Should().BeTrue();
        schema.Type!.Value.HasFlag(JsonSchemaType.Null).Should().BeTrue();
    }

    [Fact]
    public void GenerateSchema_NonNullableString_NRT_DoesNotHaveNullFlag()
    {
        var type = GetType("NullableModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "nonNullableString");

        // NonNullableString is string (NRT non-nullable) — must not include Null
        schema.Type.Should().NotBeNull();
        schema.Type!.Value.HasFlag(JsonSchemaType.Null).Should().BeFalse();
    }

    // =========================================================================
    // Enums
    // =========================================================================

    [Fact]
    public void GenerateSchema_UserStatus_NoConverter_ReturnsIntegerWithNumericValues()
    {
        var type = GetType("UserStatus");
        var schema = (OpenApiSchema)_generator.GenerateSchema(type);

        schema.Type.Should().Be(JsonSchemaType.Integer);
        schema.Enum.Should().NotBeNull();
        schema.Enum!.Count.Should().Be(4);

        var values = schema.Enum.Select(n => n!.GetValue<int>()).ToList();
        values.Should().BeEquivalentTo(new[] { 0, 1, 2, 3 });
    }

    [Fact]
    public void GenerateSchema_Priority_WithJsonStringEnumConverter_ReturnsStringWithNameValues()
    {
        var type = GetType("Priority");
        var schema = (OpenApiSchema)_generator.GenerateSchema(type);

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Enum.Should().NotBeNull();
        schema.Enum!.Count.Should().Be(4);

        var values = schema.Enum.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo("Low", "Medium", "High", "Critical");
    }

    [Fact]
    public void GenerateSchema_UserStatus_WithEnumAsStringOption_ReturnsStringType()
    {
        var generator = new SchemaGenerator(new SchemaOptions { EnumAsString = true });
        var type = GetType("UserStatus");
        var schema = (OpenApiSchema)generator.GenerateSchema(type);

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Enum.Should().NotBeNull();
        var values = schema.Enum!.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo("Active", "Suspended", "Banned", "Deleted");
    }

    // =========================================================================
    // Collections — CollectionModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_StringArray_ReturnsArrayWithStringItems()
    {
        var type = GetType("CollectionModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "tags");

        schema.Type.Should().Be(JsonSchemaType.Array);
        schema.Items.Should().NotBeNull();
        var itemSchema = (OpenApiSchema)schema.Items!;
        itemSchema.Type.Should().Be(JsonSchemaType.String);
    }

    [Fact]
    public void GenerateSchema_ListOfInt_ReturnsArrayWithIntegerItems()
    {
        var type = GetType("CollectionModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "scores");

        schema.Type.Should().Be(JsonSchemaType.Array);
        schema.Items.Should().NotBeNull();
        var itemSchema = (OpenApiSchema)schema.Items!;
        itemSchema.Type.Should().Be(JsonSchemaType.Integer);
    }

    [Fact]
    public void GenerateSchema_HashSetOfString_ReturnsArrayWithUniqueItemsTrue()
    {
        var type = GetType("CollectionModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "uniqueNames");

        schema.Type.Should().Be(JsonSchemaType.Array);
        schema.UniqueItems.Should().BeTrue();
        var itemSchema = (OpenApiSchema)schema.Items!;
        itemSchema.Type.Should().Be(JsonSchemaType.String);
    }

    [Fact]
    public void GenerateSchema_DictionaryOfStringToInt_ReturnsObjectWithIntegerAdditionalProperties()
    {
        var type = GetType("CollectionModel");
        var objectSchema = ResolveSchema(_generator.GenerateSchema(type));
        var schema = (OpenApiSchema)GetProperty(objectSchema, "counts");

        schema.Type.Should().Be(JsonSchemaType.Object);
        schema.AdditionalProperties.Should().NotBeNull();
        var valueSchema = (OpenApiSchema)schema.AdditionalProperties!;
        valueSchema.Type.Should().Be(JsonSchemaType.Integer);
    }

    // =========================================================================
    // Complex types with $ref
    // =========================================================================

    [Fact]
    public void GenerateSchema_UserDto_ReturnsOpenApiSchemaReference()
    {
        // Use a fresh generator per test for isolation
        var generator = new SchemaGenerator();
        var type = GetType("UserDto");
        var result = generator.GenerateSchema(type);

        result.Should().BeOfType<OpenApiSchemaReference>();
    }

    [Fact]
    public void GenerateSchema_UserDto_SchemaHasExpectedProperties()
    {
        var generator = new SchemaGenerator();
        var type = GetType("UserDto");
        generator.GenerateSchema(type);

        generator.Schemas.Should().ContainKey("UserDto");
        var schema = generator.Schemas["UserDto"];
        schema.Properties.Should().NotBeNull();

        var propNames = schema.Properties!.Keys.ToHashSet();
        propNames.Should().Contain("id");
        propNames.Should().Contain("email");
        propNames.Should().Contain("displayName");
        propNames.Should().Contain("status");
        propNames.Should().Contain("createdAt");
        propNames.Should().Contain("profile");
        propNames.Should().Contain("tags");
        propNames.Should().Contain("metadata");
    }

    [Fact]
    public void GenerateSchema_ApiResponseUserDto_UsesCorrectSchemaId()
    {
        var generator = new SchemaGenerator();
        // ApiResponse<T> is a generic type — need to construct it with UserDto
        var apiResponseOpenType = _loader.Assembly.GetType("SampleApi.Models.ApiResponse`1")!;
        var userDtoType = GetType("UserDto");
        var apiResponseOfUserDto = apiResponseOpenType.MakeGenericType(userDtoType);

        var result = generator.GenerateSchema(apiResponseOfUserDto);

        result.Should().BeOfType<OpenApiSchemaReference>();
        var reference = (OpenApiSchemaReference)result;
        reference.Reference.Id.Should().Be("UserDtoApiResponse");
    }

    [Fact]
    public void GenerateSchema_UserDto_ProfileProperty_IsNullableReferenceToUserProfile()
    {
        var generator = new SchemaGenerator();
        var type = GetType("UserDto");
        generator.GenerateSchema(type);

        var userDtoSchema = generator.Schemas["UserDto"];
        var profileProp = userDtoSchema.Properties!["profile"];

        // Profile is UserProfile? (nullable reference type). The generator wraps nullable
        // reference type properties in anyOf: [$ref, {type: null}].
        profileProp.Should().BeOfType<OpenApiSchema>();
        var profileSchema = (OpenApiSchema)profileProp;
        profileSchema.AnyOf.Should().NotBeNull();
        profileSchema.AnyOf!.Count.Should().Be(2);
        profileSchema.AnyOf[0].Should().BeOfType<OpenApiSchemaReference>();
        var profileRef = (OpenApiSchemaReference)profileSchema.AnyOf[0];
        profileRef.Reference.Id.Should().Be("UserProfile");
    }

    // =========================================================================
    // Inheritance — DerivedEntity
    // =========================================================================

    [Fact]
    public void GenerateSchema_DerivedEntity_IncludesInheritedAndOwnProperties()
    {
        var generator = new SchemaGenerator();
        var type = GetType("DerivedEntity");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["DerivedEntity"];
        schema.Properties.Should().NotBeNull();
        var propNames = schema.Properties!.Keys.ToHashSet();

        // Inherited from BaseEntity
        propNames.Should().Contain("id");
        propNames.Should().Contain("createdAt");

        // Own properties
        propNames.Should().Contain("name");
        propNames.Should().Contain("description");
    }

    // =========================================================================
    // Self-referencing — TreeNode
    // =========================================================================

    [Fact]
    public void GenerateSchema_TreeNode_DoesNotThrowStackOverflow()
    {
        var generator = new SchemaGenerator();
        var type = GetType("TreeNode");

        var act = () => generator.GenerateSchema(type);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateSchema_TreeNode_Children_IsNullableArrayWithTreeNodeItems()
    {
        var generator = new SchemaGenerator();
        var type = GetType("TreeNode");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["TreeNode"];
        schema.Properties.Should().NotBeNull();

        // Children is List<TreeNode>? — a nullable reference type property.
        // The generator returns an inline array schema with Null added to the type flags.
        var childrenProp = schema.Properties!["children"];
        childrenProp.Should().BeOfType<OpenApiSchema>();
        var childrenSchema = (OpenApiSchema)childrenProp;

        // Nullable array: type flags include both Array and Null
        childrenSchema.Type.Should().NotBeNull();
        childrenSchema.Type!.Value.HasFlag(JsonSchemaType.Array).Should().BeTrue();
        childrenSchema.Type!.Value.HasFlag(JsonSchemaType.Null).Should().BeTrue();
        childrenSchema.Items.Should().NotBeNull();
        childrenSchema.Items.Should().BeOfType<OpenApiSchemaReference>();

        var itemsRef = (OpenApiSchemaReference)childrenSchema.Items!;
        itemsRef.Reference.Id.Should().Be("TreeNode");
    }

    [Fact]
    public void GenerateSchema_TreeNode_Parent_IsNullableRefToTreeNode()
    {
        var generator = new SchemaGenerator();
        var type = GetType("TreeNode");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["TreeNode"];

        // Parent is TreeNode? — nullable reference type. The generator wraps nullable
        // complex-type properties in anyOf: [$ref, {type: null}].
        var parentProp = schema.Properties!["parent"];
        parentProp.Should().BeOfType<OpenApiSchema>();
        var parentSchema = (OpenApiSchema)parentProp;
        parentSchema.AnyOf.Should().NotBeNull();
        parentSchema.AnyOf!.Count.Should().Be(2);
        parentSchema.AnyOf[0].Should().BeOfType<OpenApiSchemaReference>();
        var parentRef = (OpenApiSchemaReference)parentSchema.AnyOf[0];
        parentRef.Reference.Id.Should().Be("TreeNode");
    }

    // =========================================================================
    // JSON attributes — JsonCustomModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_JsonPropertyName_UsesRenamedPropertyKey()
    {
        var generator = new SchemaGenerator();
        var type = GetType("JsonCustomModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["JsonCustomModel"];
        schema.Properties.Should().NotBeNull();

        // [JsonPropertyName("user_name")] → key is "user_name", not "userName"
        schema.Properties!.Keys.Should().Contain("user_name");
        schema.Properties.Keys.Should().NotContain("userName");
        schema.Properties.Keys.Should().NotContain("UserName");
    }

    [Fact]
    public void GenerateSchema_JsonIgnore_ExcludesAlwaysIgnoredProperty()
    {
        var generator = new SchemaGenerator();
        var type = GetType("JsonCustomModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["JsonCustomModel"];
        schema.Properties.Should().NotBeNull();

        // [JsonIgnore] with no condition → always excluded
        schema.Properties!.Keys.Should().NotContain("internalSecret");
        schema.Properties.Keys.Should().NotContain("InternalSecret");
    }

    [Fact]
    public void GenerateSchema_JsonIgnoreWhenWritingNull_IncludesProperty()
    {
        var generator = new SchemaGenerator();
        var type = GetType("JsonCustomModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["JsonCustomModel"];
        schema.Properties.Should().NotBeNull();

        // [JsonIgnore(Condition = WhenWritingNull)] → NOT fully excluded, should be present
        schema.Properties!.Keys.Should().Contain("optionalField");
    }

    [Fact]
    public void GenerateSchema_JsonRequired_IncludesPropertyInRequiredArray()
    {
        var generator = new SchemaGenerator();
        var type = GetType("JsonCustomModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["JsonCustomModel"];
        schema.Required.Should().NotBeNull();

        // [JsonRequired] on ImportantValue → must be in required array
        schema.Required!.Should().Contain("importantValue");
    }

    // =========================================================================
    // Validation attributes — ValidationModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_ValidationModel_NameProperty_IsStringType()
    {
        // The SchemaGenerator maps property types to OpenAPI types but does NOT process
        // [StringLength], [Range], or [RegularExpression] into schema constraints.
        // These tests verify the type mapping and the required array, not constraints.
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        schema.Properties.Should().NotBeNull();

        // Name is a required string property
        schema.Properties!.Should().ContainKey("name");
        var nameProp = (OpenApiSchema)schema.Properties["name"];
        nameProp.Type.Should().Be(JsonSchemaType.String);
    }

    [Fact]
    public void GenerateSchema_ValidationModel_AgeProperty_IsNullableIntegerType()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Age is int? → Nullable<int> → inline schema with Integer|Null type flags
        var ageProp = schema.Properties!["age"];
        ageProp.Should().BeOfType<OpenApiSchema>();
        var ageSchema = (OpenApiSchema)ageProp;

        ageSchema.Type.Should().NotBeNull();
        ageSchema.Type!.Value.HasFlag(JsonSchemaType.Integer).Should().BeTrue();
        ageSchema.Type!.Value.HasFlag(JsonSchemaType.Null).Should().BeTrue();
    }

    [Fact]
    public void GenerateSchema_ValidationModel_CodeProperty_IsNullableStringType()
    {
        // Code is string? (nullable reference type). The generator marks all nullable reference
        // type properties with the Null flag — string schema gets String|Null type flags.
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        schema.Properties!.Should().ContainKey("code");
        var codeProp = (OpenApiSchema)schema.Properties["code"];
        // Nullable reference string: type includes both String and Null flags
        codeProp.Type.Should().NotBeNull();
        codeProp.Type!.Value.HasFlag(JsonSchemaType.String).Should().BeTrue();
        codeProp.Type!.Value.HasFlag(JsonSchemaType.Null).Should().BeTrue();
    }

    [Fact]
    public void GenerateSchema_ValidationModel_NameWithRequired_IsInRequiredArray()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        schema.Required.Should().NotBeNull();
        schema.Required!.Should().Contain("name");
    }

    // =========================================================================
    // Generic with multiple type args — PaginatedResult<UserDto, PaginationMeta>
    // =========================================================================

    [Fact]
    public void GenerateSchema_PaginatedResult_UsesSwashbuckleSchemaId()
    {
        var generator = new SchemaGenerator();
        var openType = GetType("PaginatedResult`2");
        var userDtoType = GetType("UserDto");
        var paginationMetaType = GetType("PaginationMeta");
        var closedType = openType.MakeGenericType(userDtoType, paginationMetaType);

        var result = generator.GenerateSchema(closedType);

        result.Should().BeOfType<OpenApiSchemaReference>();
        var reference = (OpenApiSchemaReference)result;
        reference.Reference.Id.Should().Be("UserDtoAndPaginationMetaPaginatedResult");
    }

    // =========================================================================
    // SchemaOptions — CamelCase
    // =========================================================================

    [Fact]
    public void GenerateSchema_WithCamelCaseOption_PropertyNamesAreCamelCase()
    {
        // CamelCase is the default, so properties like "DisplayName" → "displayName"
        var generator = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.CamelCase });
        var type = GetType("UserDto");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["UserDto"];
        schema.Properties.Should().NotBeNull();
        schema.Properties!.Keys.Should().Contain("displayName");
        schema.Properties.Keys.Should().NotContain("DisplayName");
    }

    [Fact]
    public void GenerateSchema_WithoutCamelCaseOption_PropertyNamesRetainPascalCase()
    {
        var generator = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.Preserve });
        var type = GetType("UserDto");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["UserDto"];
        schema.Properties.Should().NotBeNull();
        schema.Properties!.Keys.Should().Contain("DisplayName");
        schema.Properties.Keys.Should().NotContain("displayName");
    }

    [Fact]
    public void GenerateSchema_WithEnumAsStringOption_ChangesEnumRepresentation()
    {
        var generator = new SchemaGenerator(new SchemaOptions { EnumAsString = true });
        var type = GetType("UserStatus");
        var schema = (OpenApiSchema)generator.GenerateSchema(type);

        schema.Type.Should().Be(JsonSchemaType.String);
        var values = schema.Enum!.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo("Active", "Suspended", "Banned", "Deleted");
    }

    // =========================================================================
    // Schemas dictionary — GenerateSchema registers complex types
    // =========================================================================

    [Fact]
    public void Schemas_AfterGeneratingComplexType_ContainsRegisteredSchema()
    {
        var generator = new SchemaGenerator();
        var type = GetType("UserDto");
        generator.GenerateSchema(type);

        generator.Schemas.Should().ContainKey("UserDto");
    }

    [Fact]
    public void Schemas_AfterGeneratingUserDto_AlsoRegistersUserProfile()
    {
        var generator = new SchemaGenerator();
        var type = GetType("UserDto");
        generator.GenerateSchema(type);

        // UserDto has a UserProfile? property, so UserProfile should also be registered
        generator.Schemas.Should().ContainKey("UserProfile");
    }

    [Fact]
    public void GenerateSchema_CalledTwiceForSameType_ReturnsSameReference()
    {
        var generator = new SchemaGenerator();
        var type = GetType("UserDto");
        var first = generator.GenerateSchema(type);
        var second = generator.GenerateSchema(type);

        first.Should().BeOfType<OpenApiSchemaReference>();
        second.Should().BeOfType<OpenApiSchemaReference>();
        ((OpenApiSchemaReference)first).Reference.Id.Should().Be(
            ((OpenApiSchemaReference)second).Reference.Id);
    }

    // =========================================================================
    // Primitive types — direct call (not via complex type property)
    // =========================================================================

    [Fact]
    public void GenerateSchema_DirectlyOnStringType_ReturnsInlineSchema()
    {
        // String is a primitive, so GenerateSchema on the runtime string type works.
        // We can get System.String through the loader context.
        var stringType = _loader.FindType("System.String")!;
        var schema = (OpenApiSchema)_generator.GenerateSchema(stringType);

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().BeNull();
    }

    [Fact]
    public void GenerateSchema_DirectlyOnGuidType_ReturnsInlineSchema()
    {
        var guidType = _loader.FindType("System.Guid")!;
        var schema = (OpenApiSchema)_generator.GenerateSchema(guidType);

        schema.Type.Should().Be(JsonSchemaType.String);
        schema.Format.Should().Be("uuid");
    }

    // =========================================================================
    // Validation attribute constraints — ValidationModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_StringLength_SetsMaxLengthAndMinLength()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Name has [StringLength(100, MinimumLength = 3)]
        var nameProp = (OpenApiSchema)schema.Properties!["name"];
        nameProp.MaxLength.Should().Be(100);
        nameProp.MinLength.Should().Be(3);
    }

    [Fact]
    public void GenerateSchema_StringLengthWithNoMinimum_SetsOnlyMaxLength()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Email has [StringLength(255)] with no MinimumLength
        var emailProp = (OpenApiSchema)schema.Properties!["email"];
        emailProp.MaxLength.Should().Be(255);
        emailProp.MinLength.Should().BeNull();
    }

    [Fact]
    public void GenerateSchema_Range_SetsMinimumAndMaximum()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Age has [Range(0, 150)] and is int? (Nullable<int>) → inline schema
        var ageProp = (OpenApiSchema)schema.Properties!["age"];
        ageProp.Minimum.Should().Be("0");
        ageProp.Maximum.Should().Be("150");
    }

    [Fact]
    public void GenerateSchema_RegularExpression_SetsPattern()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Code has [RegularExpression(@"^[A-Z]{2}\d{4}$")]
        var codeProp = (OpenApiSchema)schema.Properties!["code"];
        codeProp.Pattern.Should().Be(@"^[A-Z]{2}\d{4}$");
    }

    [Fact]
    public void GenerateSchema_MaxLength_OnArrayProperty_SetsMaxItems()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Items is List<string> with [MaxLength(10)] → maxItems: 10
        var itemsProp = (OpenApiSchema)schema.Properties!["items"];
        itemsProp.MaxItems.Should().Be(10);
        itemsProp.MaxLength.Should().BeNull();
    }

    [Fact]
    public void GenerateSchema_MinLength_OnStringProperty_SetsMinLength()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // LongText has [MinLength(5)]
        var longTextProp = (OpenApiSchema)schema.Properties!["longText"];
        longTextProp.MinLength.Should().Be(5);
    }

    [Fact]
    public void GenerateSchema_EmailAddress_SetsFormatEmail()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Email has [EmailAddress]
        var emailProp = (OpenApiSchema)schema.Properties!["email"];
        emailProp.Format.Should().Be("email");
    }

    // =========================================================================
    // Extended validation attributes — ExtendedValidationModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_Url_SetsFormatUri()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        var websiteProp = (OpenApiSchema)schema.Properties!["website"];
        websiteProp.Format.Should().Be("uri");
    }

    [Fact]
    public void GenerateSchema_Phone_SetsFormatPhone()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        var phoneProp = (OpenApiSchema)schema.Properties!["phoneNumber"];
        phoneProp.Format.Should().Be("phone");
    }

    [Fact]
    public void GenerateSchema_DefaultValueInt_SetsDefaultNode()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        // Score has [DefaultValue(42)]
        var scoreProp = (OpenApiSchema)schema.Properties!["score"];
        scoreProp.Default.Should().NotBeNull();
        scoreProp.Default!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void GenerateSchema_DefaultValueBool_SetsDefaultNode()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        // IsActive has [DefaultValue(true)]
        var isActiveProp = (OpenApiSchema)schema.Properties!["isActive"];
        isActiveProp.Default.Should().NotBeNull();
        isActiveProp.Default!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void GenerateSchema_Obsolete_SetsDeprecatedTrue()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        // OldField has [Obsolete]
        var oldFieldProp = (OpenApiSchema)schema.Properties!["oldField"];
        oldFieldProp.Deprecated.Should().BeTrue();
    }

    [Fact]
    public void GenerateSchema_NonObsoleteProperty_DeprecatedIsFalse()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        // Website has no [Obsolete]
        var websiteProp = (OpenApiSchema)schema.Properties!["website"];
        websiteProp.Deprecated.Should().BeFalse();
    }

    [Fact]
    public void GenerateSchema_Description_SetsDescriptionText()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        // Alias has [Description("The user's display alias")]
        var aliasProp = (OpenApiSchema)schema.Properties!["alias"];
        aliasProp.Description.Should().Be("The user's display alias");
    }

    [Fact]
    public void GenerateSchema_MinLength_OnArrayProperty_SetsMinItems()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        // RequiredItems has [MinLength(2)] and is List<string> → minItems: 2
        var requiredItemsProp = (OpenApiSchema)schema.Properties!["requiredItems"];
        requiredItemsProp.MinItems.Should().Be(2);
        requiredItemsProp.MinLength.Should().BeNull();
    }

    [Fact]
    public void GenerateSchema_RangeDouble_SetsMinimumAndMaximum()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ExtendedValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ExtendedValidationModel"];
        // Ratio has [Range(0.0, 1.0)] and is double? (Nullable<double>) → inline schema
        var ratioProp = (OpenApiSchema)schema.Properties!["ratio"];
        ratioProp.Minimum.Should().NotBeNull();
        ratioProp.Maximum.Should().NotBeNull();
    }

    // =========================================================================
    // [JsonUnmappedMemberHandling(Disallow)] — StrictModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_JsonUnmappedMemberHandlingDisallow_SetsAdditionalPropertiesAllowedFalse()
    {
        var generator = new SchemaGenerator();
        var type = GetType("StrictModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["StrictModel"];
        schema.AdditionalPropertiesAllowed.Should().BeFalse();
    }

    [Fact]
    public void GenerateSchema_TypeWithoutUnmappedHandling_AdditionalPropertiesAllowedIsTrue()
    {
        var generator = new SchemaGenerator();
        var type = GetType("ValidationModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["ValidationModel"];
        // Default OpenApiSchema.AdditionalPropertiesAllowed is true
        schema.AdditionalPropertiesAllowed.Should().BeTrue();
    }

    // =========================================================================
    // DefaultValue on JsonCustomModel
    // =========================================================================

    [Fact]
    public void GenerateSchema_JsonCustomModel_DefaultValueInt_SetsDefaultNode()
    {
        var generator = new SchemaGenerator();
        var type = GetType("JsonCustomModel");
        generator.GenerateSchema(type);

        var schema = generator.Schemas["JsonCustomModel"];
        // DefaultNumber has [DefaultValue(42)]
        var defaultNumberProp = (OpenApiSchema)schema.Properties!["defaultNumber"];
        defaultNumberProp.Default.Should().NotBeNull();
        defaultNumberProp.Default!.GetValue<int>().Should().Be(42);
    }
}
