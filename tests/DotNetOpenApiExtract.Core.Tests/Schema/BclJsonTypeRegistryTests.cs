using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Unit tests for <see cref="BclJsonTypeRegistry"/> — verifies that well-known BCL JSON
/// container type full names map to the correct shape templates, and that
/// <see cref="BclJsonTypeRegistry.CreateSchema"/> produces the expected inline schemas.
/// </summary>
public sealed class BclJsonTypeRegistryTests
{
    // =========================================================================
    // TryGet — Any shape types
    // =========================================================================

    [Theory]
    [InlineData("System.Text.Json.JsonElement")]
    [InlineData("System.Text.Json.Nodes.JsonNode")]
    [InlineData("System.Text.Json.Nodes.JsonValue")]
    [InlineData("System.Text.Json.JsonDocument")]
    [InlineData("Newtonsoft.Json.Linq.JToken")]
    [InlineData("Newtonsoft.Json.Linq.JValue")]
    [InlineData("Newtonsoft.Json.Linq.JRaw")]
    public void TryGet_KnownAnyType_ReturnsAnyTemplate(string fullName)
    {
        var template = BclJsonTypeRegistry.TryGet(fullName);

        template.Should().NotBeNull();
        template!.Shape.Should().Be(BclJsonTypeRegistry.BclJsonShape.Any);
        template.DefaultDescription.Should().Be("Arbitrary JSON value");
    }

    // =========================================================================
    // TryGet — Object shape types
    // =========================================================================

    [Theory]
    [InlineData("System.Text.Json.Nodes.JsonObject")]
    [InlineData("Newtonsoft.Json.Linq.JObject")]
    [InlineData("System.Dynamic.ExpandoObject")]
    public void TryGet_KnownObjectType_ReturnsObjectTemplate(string fullName)
    {
        var template = BclJsonTypeRegistry.TryGet(fullName);

        template.Should().NotBeNull();
        template!.Shape.Should().Be(BclJsonTypeRegistry.BclJsonShape.Object);
        template.DefaultDescription.Should().Be("Arbitrary JSON object");
    }

    // =========================================================================
    // TryGet — Array shape types
    // =========================================================================

    [Theory]
    [InlineData("System.Text.Json.Nodes.JsonArray")]
    [InlineData("Newtonsoft.Json.Linq.JArray")]
    public void TryGet_KnownArrayType_ReturnsArrayTemplate(string fullName)
    {
        var template = BclJsonTypeRegistry.TryGet(fullName);

        template.Should().NotBeNull();
        template!.Shape.Should().Be(BclJsonTypeRegistry.BclJsonShape.Array);
        template.DefaultDescription.Should().Be("Arbitrary JSON array");
    }

    // =========================================================================
    // TryGet — Unknown types
    // =========================================================================

    [Theory]
    [InlineData("System.Object")]
    [InlineData("System.String")]
    [InlineData("Newtonsoft.Json.Linq.JProperty")]
    [InlineData("System.Dynamic.DynamicObject")]
    [InlineData("")]
    [InlineData("completely.unknown.Type")]
    public void TryGet_UnknownType_ReturnsNull(string fullName)
    {
        var template = BclJsonTypeRegistry.TryGet(fullName);
        template.Should().BeNull();
    }

    [Fact]
    public void TryGet_NullInput_ReturnsNull()
    {
        var template = BclJsonTypeRegistry.TryGet(null!);
        template.Should().BeNull();
    }

    // =========================================================================
    // CreateSchema — Any shape
    // =========================================================================

    [Fact]
    public void CreateSchema_Any_ReturnsTypelessSchemaWithAnyValueDescription()
    {
        var template = BclJsonTypeRegistry.TryGet("System.Text.Json.JsonElement")!;
        var schema = BclJsonTypeRegistry.CreateSchema(template);

        schema.Type.Should().BeNull("truly-any schema must not have a type");
        schema.Properties.Should().BeNullOrEmpty();
        schema.Items.Should().BeNull();
        schema.AdditionalProperties.Should().BeNull();
        schema.Description.Should().Be("Arbitrary JSON value");
    }

    // =========================================================================
    // CreateSchema — Object shape
    // =========================================================================

    [Fact]
    public void CreateSchema_Object_ReturnsObjectWithAdditionalPropertiesEmpty()
    {
        var template = BclJsonTypeRegistry.TryGet("System.Text.Json.Nodes.JsonObject")!;
        var schema = BclJsonTypeRegistry.CreateSchema(template);

        schema.Type.Should().Be(JsonSchemaType.Object);
        schema.AdditionalProperties.Should().NotBeNull("additionalProperties must be an explicit inline schema");
        var addlProps = (OpenApiSchema)schema.AdditionalProperties!;
        addlProps.Type.Should().BeNull("additionalProperties schema must be truly-any ({})");
        addlProps.Properties.Should().BeNullOrEmpty();
        schema.Properties.Should().BeNullOrEmpty();
        schema.Description.Should().Be("Arbitrary JSON object");
    }

    // =========================================================================
    // CreateSchema — Array shape
    // =========================================================================

    [Fact]
    public void CreateSchema_Array_ReturnsArrayWithItemsEmpty()
    {
        var template = BclJsonTypeRegistry.TryGet("System.Text.Json.Nodes.JsonArray")!;
        var schema = BclJsonTypeRegistry.CreateSchema(template);

        schema.Type.Should().Be(JsonSchemaType.Array);
        schema.Items.Should().NotBeNull("items must be an explicit inline schema");
        var items = (OpenApiSchema)schema.Items!;
        items.Type.Should().BeNull("items schema must be truly-any ({})");
        items.Properties.Should().BeNullOrEmpty();
        schema.Properties.Should().BeNullOrEmpty();
        schema.Description.Should().Be("Arbitrary JSON array");
    }
}
