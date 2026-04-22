using AwesomeAssertions;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests covering three extractor bug fixes:
/// <list type="bullet">
///   <item>Bug 1 — <c>[SwaggerRequestBody(Description = "...")]</c> on body parameters.</item>
///   <item>Bug 2 — XML property descriptions on closed generic specializations.</item>
///   <item>Bug 3 — <c>schema.Default</c> from <c>[DefaultValue]</c> and inline C# defaults.</item>
/// </list>
/// </summary>
public sealed class BugFixIntegrationTests
{
    private readonly OpenApiDocument _document;

    public BugFixIntegrationTests()
    {
        _document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        });
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private OpenApiOperation FindOperation(string path, HttpMethod httpMethod)
    {
        if (!_document.Paths.TryGetValue(path, out var pathItemInterface))
            throw new InvalidOperationException(
                $"Path '{path}' not found. Available: {string.Join(", ", _document.Paths.Keys.OrderBy(p => p))}");

        var pathItem = pathItemInterface as OpenApiPathItem
            ?? throw new InvalidOperationException($"Path item for '{path}' is not an OpenApiPathItem.");

        if (pathItem.Operations == null || !pathItem.Operations.TryGetValue(httpMethod, out var operation))
            throw new InvalidOperationException(
                $"No {httpMethod.Method} operation at '{path}'.");

        return operation;
    }

    private OpenApiSchema ResolveComponentSchema(string schemaId)
    {
        var schemas = _document.Components?.Schemas
            ?? throw new InvalidOperationException("document.Components.Schemas is null.");

        if (!schemas.TryGetValue(schemaId, out var schemaInterface))
            throw new InvalidOperationException(
                $"Schema '{schemaId}' not found. Available: {string.Join(", ", schemas.Keys.OrderBy(k => k))}");

        return schemaInterface as OpenApiSchema
            ?? throw new InvalidOperationException(
                $"Schema '{schemaId}' is not a concrete OpenApiSchema.");
    }

    // =========================================================================
    // Bug 1 — [SwaggerRequestBody] description propagates to RequestBody
    // =========================================================================

    /// <summary>
    /// <c>[SwaggerRequestBody("User creation payload")]</c> on the <c>request</c> parameter of
    /// <c>UsersController.CreateUser</c> must appear as <c>operation.RequestBody.Description</c>.
    /// </summary>
    [Fact]
    public void CreateUser_RequestBody_HasSwaggerRequestBodyDescription()
    {
        var operation = FindOperation("/api/v1/users", HttpMethod.Post);

        operation.RequestBody.Should().NotBeNull();
        var requestBody = operation.RequestBody as OpenApiRequestBody
            ?? throw new InvalidOperationException("RequestBody is not OpenApiRequestBody.");

        requestBody.Description.Should().Be("User creation payload",
            because: "[SwaggerRequestBody(\"User creation payload\")] must populate RequestBody.Description");
    }

    // =========================================================================
    // Bug 1 (named-arg form) — [SwaggerRequestBody(Description = "...")] propagates
    // =========================================================================

    /// <summary>
    /// <c>[SwaggerRequestBody(Description = "User update payload (named-arg form)")]</c> on the
    /// <c>request</c> parameter of <c>UsersController.UpdateUser</c> must appear as
    /// <c>operation.RequestBody.Description</c>.
    /// </summary>
    [Fact]
    public void UpdateUser_RequestBody_HasNamedArgSwaggerRequestBodyDescription()
    {
        var operation = FindOperation("/api/v1/users/{id}", HttpMethod.Put);

        operation.RequestBody.Should().NotBeNull();
        var requestBody = operation.RequestBody as OpenApiRequestBody
            ?? throw new InvalidOperationException("RequestBody is not OpenApiRequestBody.");

        requestBody.Description.Should().Be("User update payload (named-arg form)",
            because: "[SwaggerRequestBody(Description = \"...\")] named-arg form must populate RequestBody.Description");
    }

    // =========================================================================
    // Bug 2 — Closed generic schema properties carry XML doc descriptions
    // =========================================================================

    /// <summary>
    /// The closed generic schema <c>ApiResponse&lt;UserDto&gt;</c> (schema ID: UserDtoApiResponse)
    /// must expose the property descriptions declared on the open generic
    /// <c>ApiResponse&lt;T&gt;</c> in the XML doc.
    /// </summary>
    [Fact]
    public void UserDtoApiResponse_SuccessProperty_HasXmlDocDescription()
    {
        var schema = ResolveComponentSchema("UserDtoApiResponse");
        schema.Properties.Should().ContainKey("success");

        var successSchema = schema.Properties!["success"] as OpenApiSchema;
        successSchema.Should().NotBeNull();
        successSchema!.Description.Should().NotBeNullOrEmpty(
            because: "ApiResponse<T>.Success has XML <summary> 'Whether the operation was successful'");
        successSchema.Description.Should().Contain("successful");
    }

    [Fact]
    public void UserDtoApiResponse_DataProperty_HasXmlDocDescription()
    {
        var schema = ResolveComponentSchema("UserDtoApiResponse");
        schema.Properties.Should().ContainKey("data");

        var dataSchema = schema.Properties!["data"] as OpenApiSchema;
        dataSchema.Should().NotBeNull();
        dataSchema!.Description.Should().NotBeNullOrEmpty(
            because: "ApiResponse<T>.Data has XML <summary> 'Response data (null on error)'");
    }

    /// <summary>
    /// The doubly-nested closed generic <c>ApiResponse&lt;List&lt;UserDto&gt;&gt;</c>
    /// (schema ID: UserDtoListApiResponse) must also carry property descriptions from the
    /// open generic definition.
    /// </summary>
    [Fact]
    public void UserDtoListApiResponse_SuccessProperty_HasXmlDocDescription()
    {
        var schema = ResolveComponentSchema("UserDtoListApiResponse");
        schema.Properties.Should().ContainKey("success");

        var successSchema = schema.Properties!["success"] as OpenApiSchema;
        successSchema.Should().NotBeNull();
        successSchema!.Description.Should().NotBeNullOrEmpty(
            because: "ApiResponse<List<UserDto>>.Success must inherit description from open generic ApiResponse<T>.Success");
        successSchema.Description.Should().Contain("successful");
    }

    // =========================================================================
    // Bug 3 — schema.Default written from [DefaultValue] attribute and inline default
    // =========================================================================

    /// <summary>
    /// <c>GetUsers</c> uses inline defaults (<c>int page = 1</c>, <c>int pageSize = 20</c>).
    /// Both query parameter schemas must have <c>schema.default</c> set.
    /// </summary>
    [Fact]
    public void GetUsers_PageParam_SchemaDefaultFromInlineDefault()
    {
        var operation = FindOperation("/api/v1/users", HttpMethod.Get);

        var page = operation.Parameters
            ?.OfType<OpenApiParameter>()
            .SingleOrDefault(p => p.Name == "page");
        page.Should().NotBeNull("GetUsers must have a 'page' query parameter");

        var schema = page!.Schema as OpenApiSchema;
        schema.Should().NotBeNull();
        schema!.Default.Should().NotBeNull(
            because: "page = 1 inline default must be written to schema.Default");
        schema.Default!.ToString().Should().Be("1");
    }

    [Fact]
    public void GetUsers_PageSizeParam_SchemaDefaultFromInlineDefault()
    {
        var operation = FindOperation("/api/v1/users", HttpMethod.Get);

        var pageSize = operation.Parameters
            ?.OfType<OpenApiParameter>()
            .SingleOrDefault(p => p.Name == "pageSize");
        pageSize.Should().NotBeNull("GetUsers must have a 'pageSize' query parameter");

        var schema = pageSize!.Schema as OpenApiSchema;
        schema.Should().NotBeNull();
        schema!.Default.Should().NotBeNull(
            because: "pageSize = 20 inline default must be written to schema.Default");
        schema.Default!.ToString().Should().Be("20");
    }

    /// <summary>
    /// <c>SearchUsers</c> uses <c>[DefaultValue(1)]</c> / <c>[DefaultValue(20)]</c> on its
    /// query parameters. Both schemas must have <c>schema.default</c> set via the attribute path.
    /// </summary>
    [Fact]
    public void SearchUsers_PageParam_SchemaDefaultFromDefaultValueAttribute()
    {
        var operation = FindOperation("/api/v1/users/search", HttpMethod.Get);

        var page = operation.Parameters
            ?.OfType<OpenApiParameter>()
            .SingleOrDefault(p => p.Name == "page");
        page.Should().NotBeNull("SearchUsers must have a 'page' query parameter");

        var schema = page!.Schema as OpenApiSchema;
        schema.Should().NotBeNull();
        schema!.Default.Should().NotBeNull(
            because: "[DefaultValue(1)] must be written to schema.Default");
        schema.Default!.ToString().Should().Be("1");
    }

    [Fact]
    public void SearchUsers_PageSizeParam_SchemaDefaultFromDefaultValueAttribute()
    {
        var operation = FindOperation("/api/v1/users/search", HttpMethod.Get);

        var pageSize = operation.Parameters
            ?.OfType<OpenApiParameter>()
            .SingleOrDefault(p => p.Name == "pageSize");
        pageSize.Should().NotBeNull("SearchUsers must have a 'pageSize' query parameter");

        var schema = pageSize!.Schema as OpenApiSchema;
        schema.Should().NotBeNull();
        schema!.Default.Should().NotBeNull(
            because: "[DefaultValue(20)] must be written to schema.Default");
        schema.Default!.ToString().Should().Be("20");
    }

    // =========================================================================
    // Bug 3 (required=false) — [DefaultValue] implies the parameter is optional
    // =========================================================================

    /// <summary>
    /// <c>SearchUsers.page</c> carries <c>[DefaultValue(1)]</c> but no inline C# default.
    /// The parameter must be marked <c>required: false</c> because a default value
    /// implies the caller may omit the argument.
    /// </summary>
    [Fact]
    public void SearchUsers_PageParam_IsNotRequired()
    {
        var operation = FindOperation("/api/v1/users/search", HttpMethod.Get);

        var page = operation.Parameters
            ?.OfType<OpenApiParameter>()
            .SingleOrDefault(p => p.Name == "page");
        page.Should().NotBeNull("SearchUsers must have a 'page' query parameter");

        page!.Required.Should().BeFalse(
            because: "[DefaultValue(1)] on a non-path parameter must set required to false");
    }

    /// <summary>
    /// <c>SearchUsers.pageSize</c> carries <c>[DefaultValue(20)]</c> but no inline C# default.
    /// The parameter must be marked <c>required: false</c>.
    /// </summary>
    [Fact]
    public void SearchUsers_PageSizeParam_IsNotRequired()
    {
        var operation = FindOperation("/api/v1/users/search", HttpMethod.Get);

        var pageSize = operation.Parameters
            ?.OfType<OpenApiParameter>()
            .SingleOrDefault(p => p.Name == "pageSize");
        pageSize.Should().NotBeNull("SearchUsers must have a 'pageSize' query parameter");

        pageSize!.Required.Should().BeFalse(
            because: "[DefaultValue(20)] on a non-path parameter must set required to false");
    }
}
