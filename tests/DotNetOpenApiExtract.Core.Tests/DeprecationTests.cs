using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Loading;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests verifying that <c>[Obsolete]</c> propagates to
/// <c>deprecated: true</c> in the generated OpenAPI document at operation-level
/// (action and controller) and at schema-level (DTO class).
/// </summary>
public sealed class DeprecationTests : IDisposable
{
    private readonly AssemblyLoader _loader;
    private readonly OpenApiDocument _document;

    public DeprecationTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };
        _document = OpenApiDocumentBuilder.Build(options);
    }

    public void Dispose() => _loader.Dispose();

    // =========================================================================
    // Action-level [Obsolete] — regression
    // =========================================================================

    [Fact]
    public void ObsoleteOnAction_OperationIsDeprecated()
    {
        // ProductsController.LegacySearch has [Obsolete]
        var op = GetOperation(HttpMethod.Get, "/api/v1/products/legacysearch");
        op.Deprecated.Should().BeTrue();
    }

    [Fact]
    public void NonObsoleteAction_OperationNotDeprecated()
    {
        // ProductsController.List does NOT have [Obsolete]
        var op = GetOperation(HttpMethod.Get, "/api/v1/products/list");
        op.Deprecated.Should().BeFalse();
    }

    // =========================================================================
    // Controller-level [Obsolete] — all actions are deprecated
    // =========================================================================

    [Fact]
    public void ObsoleteOnController_GetAction_IsDeprecated()
    {
        // DeprecatedController.Get — controller has [Obsolete], action does not
        var op = GetOperation(HttpMethod.Get, "/api/deprecated");
        op.Deprecated.Should().BeTrue();
    }

    [Fact]
    public void ObsoleteOnController_PostAction_IsDeprecated()
    {
        // DeprecatedController.Post — controller has [Obsolete], action does not
        var op = GetOperation(HttpMethod.Post, "/api/deprecated");
        op.Deprecated.Should().BeTrue();
    }

    [Fact]
    public void ObsoleteOnController_DoesNotAffectOtherControllers()
    {
        // UsersController has no [Obsolete] — its GET action must NOT be deprecated
        var op = GetOperation(HttpMethod.Get, "/api/v1/users");
        op.Deprecated.Should().BeFalse();
    }

    // =========================================================================
    // DTO class-level [Obsolete] → schema.deprecated: true
    // =========================================================================

    [Fact]
    public void ObsoleteOnDtoClass_SchemaIsDeprecated()
    {
        _document.Components.Should().NotBeNull();
        _document.Components!.Schemas.Should().ContainKey("ObsoleteDto");

        var schema = _document.Components.Schemas["ObsoleteDto"] as OpenApiSchema;
        schema.Should().NotBeNull("ObsoleteDto schema should be an inline OpenApiSchema in Components.Schemas");
        schema!.Deprecated.Should().BeTrue();
    }

    [Fact]
    public void NonObsoleteDto_SchemaNotDeprecated()
    {
        _document.Components.Should().NotBeNull();
        // ProductDto is a regular DTO with no [Obsolete]
        _document.Components!.Schemas.Should().ContainKey("ProductDto");

        var schema = _document.Components.Schemas["ProductDto"] as OpenApiSchema;
        schema.Should().NotBeNull();
        schema!.Deprecated.Should().BeFalse();
    }

    // =========================================================================
    // Property-level [Obsolete] → individual property schema deprecated: true
    // =========================================================================

    [Fact]
    public void ObsoleteOnDtoProperty_PropertySchemaIsDeprecated()
    {
        // ExtendedValidationModel.OldField has [Obsolete("Use NewField instead")].
        // The property type is string? so its property schema is an inline OpenApiSchema
        // (primitives are never $ref-wrapped) → ApplyValidationAttributes fires.
        // We call SchemaGenerator directly to avoid requiring a controller fixture.
        var extType = _loader.Assembly.GetType("SampleApi.Models.ExtendedValidationModel");
        extType.Should().NotBeNull("ExtendedValidationModel must exist in SampleApi.Models");

        var generator = new DotNetOpenApiExtract.Core.Schema.SchemaGenerator();
        generator.GenerateSchema(extType!); // populates Schemas["ExtendedValidationModel"]

        generator.Schemas.Should().ContainKey("ExtendedValidationModel");
        var schema = generator.Schemas["ExtendedValidationModel"];
        schema.Properties.Should().ContainKey("oldField",
            "OldField serialized to camelCase 'oldField'");

        var propSchema = schema.Properties!["oldField"] as OpenApiSchema;
        propSchema.Should().NotBeNull("the property schema for oldField must be an inline OpenApiSchema");
        propSchema!.Deprecated.Should().BeTrue(
            "OldField is decorated with [Obsolete] — its property schema must have deprecated: true");
    }

    // =========================================================================
    // Enum type-level [Obsolete] → schema.deprecated: true
    // =========================================================================

    [Fact]
    public void ObsoleteOnEnumType_SchemaIsDeprecated()
    {
        // ObsoleteEnum is declared in SampleApi.Models with [Obsolete].
        // Enums are emitted as inline schemas (not registered in Components.Schemas),
        // so we load the type directly via AssemblyLoader and ask SchemaGenerator for it.
        var enumType = _loader.Assembly.GetType("SampleApi.Models.ObsoleteEnum");
        enumType.Should().NotBeNull("ObsoleteEnum must exist in SampleApi.Models");

        var generator = new DotNetOpenApiExtract.Core.Schema.SchemaGenerator();
        var schema = generator.GenerateSchema(enumType!) as OpenApiSchema;

        schema.Should().NotBeNull("ObsoleteEnum schema should be an inline OpenApiSchema");
        schema!.Deprecated.Should().BeTrue("enum decorated with [Obsolete] must have deprecated: true");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private OpenApiOperation GetOperation(HttpMethod method, string path)
    {
        _document.Paths.Should().ContainKey(path, $"path '{path}' must exist in the document");
        var pathItem = _document.Paths[path] as OpenApiPathItem;
        pathItem.Should().NotBeNull($"path item for '{path}' must be an OpenApiPathItem");

        pathItem!.Operations.Should().ContainKey(method,
            $"path '{path}' must have a {method.Method} operation");
        return pathItem.Operations[method];
    }
}
