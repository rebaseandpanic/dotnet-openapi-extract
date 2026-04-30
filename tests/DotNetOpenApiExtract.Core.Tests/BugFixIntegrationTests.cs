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

    // =========================================================================
    // Bug A — Nested-type XML doc descriptions propagate to component schemas
    // =========================================================================

    /// <summary>
    /// <c>NestedDtoController.ServiceDto</c> is declared as a nested type inside a controller.
    /// Reflection emits its FullName with '+' (e.g. "NestedDtoController+ServiceDto") while
    /// the XML compiler uses '.' — after the fix the component schema's properties must have
    /// descriptions from the XML doc.
    /// </summary>
    [Fact]
    public void ServiceDto_NameProperty_HasXmlDocDescriptionFromNestedType()
    {
        var schema = ResolveComponentSchema("ServiceDto");
        schema.Properties.Should().ContainKey("name");

        var nameSchema = schema.Properties!["name"] as OpenApiSchema;
        nameSchema.Should().NotBeNull();
        nameSchema!.Description.Should().Be("Service name",
            because: "NestedDtoController.ServiceDto.Name has XML <summary> 'Service name'");
    }

    [Fact]
    public void ServiceDto_EndpointProperty_HasXmlDocDescriptionFromNestedType()
    {
        var schema = ResolveComponentSchema("ServiceDto");
        schema.Properties.Should().ContainKey("endpoint");

        var endpointSchema = schema.Properties!["endpoint"] as OpenApiSchema;
        endpointSchema.Should().NotBeNull();
        endpointSchema!.Description.Should().Be("Service endpoint URL",
            because: "NestedDtoController.ServiceDto.Endpoint has XML <summary> 'Service endpoint URL'");
    }

    [Fact]
    public void ServiceResponse_SuccessProperty_HasXmlDocDescriptionFromNestedType()
    {
        var schema = ResolveComponentSchema("ServiceResponse");
        schema.Properties.Should().ContainKey("success");

        var successSchema = schema.Properties!["success"] as OpenApiSchema;
        successSchema.Should().NotBeNull();
        successSchema!.Description.Should().Be("Whether the service call succeeded",
            because: "NestedDtoController.ServiceResponse.Success has XML <summary>");
    }

    /// <summary>
    /// <c>NestedDtoController.ServiceDto.ServiceEndpoints</c> is a three-level nesting:
    /// controller → DTO → inner class. Reflection emits
    /// "NestedDtoController+ServiceDto+ServiceEndpoints" while the XML compiler emits
    /// "NestedDtoController.ServiceDto.ServiceEndpoints". After the fix the schema must
    /// resolve both the type-level and property-level descriptions.
    /// </summary>
    [Fact]
    public void NestedDto_DeeplyNestedThreeLevel_ResolvesXmlDescriptions()
    {
        var schema = ResolveComponentSchema("ServiceEndpoints");
        schema.Description.Should().NotBeNullOrEmpty(
            because: "NestedDtoController.ServiceDto.ServiceEndpoints has XML <summary> on the class");

        var healthPath = schema.Properties!["healthPath"] as OpenApiSchema;
        healthPath.Should().NotBeNull();
        healthPath!.Description.Should().Be("Health probe path",
            because: "ServiceEndpoints.HealthPath has XML <summary> 'Health probe path'");
    }

    // =========================================================================
    // Bug B — Inherited property descriptions propagate from base type XML docs
    // =========================================================================

    /// <summary>
    /// <c>CreateServerRequest</c> inherits <c>Host</c> and <c>Port</c> from <c>ServerRequestBase</c>.
    /// The XML doc for those properties lives under "P:SampleApi.Models.ServerRequestBase.Host" etc.
    /// Before the fix, looking up the property with the leaf type (CreateServerRequest) misses.
    /// </summary>
    [Fact]
    public void CreateServerRequest_HostProperty_HasInheritedXmlDocDescription()
    {
        var schema = ResolveComponentSchema("CreateServerRequest");
        schema.Properties.Should().ContainKey("host");

        var hostSchema = schema.Properties!["host"] as OpenApiSchema;
        hostSchema.Should().NotBeNull();
        hostSchema!.Description.Should().Be("Host name of the target server",
            because: "CreateServerRequest.Host is inherited from ServerRequestBase and has XML <summary> on the base");
    }

    [Fact]
    public void CreateServerRequest_PortProperty_HasInheritedXmlDocDescription()
    {
        var schema = ResolveComponentSchema("CreateServerRequest");
        schema.Properties.Should().ContainKey("port");

        var portSchema = schema.Properties!["port"] as OpenApiSchema;
        portSchema.Should().NotBeNull();
        portSchema!.Description.Should().Be("Port number to connect on",
            because: "CreateServerRequest.Port is inherited from ServerRequestBase and has XML <summary> on the base");
    }

    [Fact]
    public void CreateServerRequest_LabelProperty_HasOwnXmlDocDescription()
    {
        // Label is declared on the derived type itself — must also resolve correctly.
        var schema = ResolveComponentSchema("CreateServerRequest");
        schema.Properties.Should().ContainKey("label");

        var labelSchema = schema.Properties!["label"] as OpenApiSchema;
        labelSchema.Should().NotBeNull();
        labelSchema!.Description.Should().Be("Display label for the new server",
            because: "CreateServerRequest.Label is declared on the derived type and has XML <summary>");
    }

    // =========================================================================
    // Positional record primary-ctor parameter attributes — end-to-end through
    // OpenApiDocumentBuilder so XML <param> → property description and merged
    // [Description] paths are exercised.
    // =========================================================================

    /// <summary>
    /// <c>record CreatePositionalCustomerRequest([Required, StringLength(100, MinimumLength = 2)] string Name, ...)</c>:
    /// the primary-ctor parameter carries the validation attributes. After the merge,
    /// the component schema must list <c>name</c> in <c>required</c> and apply
    /// <c>maxLength</c>/<c>minLength</c>.
    /// </summary>
    [Fact]
    public void CreatePositionalCustomerRequest_NameProperty_HasValidationFromCtorParam()
    {
        var schema = ResolveComponentSchema("CreatePositionalCustomerRequest");

        schema.Required.Should().NotBeNull();
        schema.Required!.Should().Contain("name",
            because: "[Required] on the positional ctor param must reach the schema");

        var nameProp = (OpenApiSchema)schema.Properties!["name"];
        nameProp.MaxLength.Should().Be(100);
        nameProp.MinLength.Should().Be(2);
    }

    /// <summary>
    /// XML <c>&lt;param name="Name"&gt;Customer full name&lt;/param&gt;</c> on the record's
    /// type-level XML doc must end up as the <c>name</c> property's description in the
    /// component schema. The C# compiler propagates &lt;param&gt; into a synthesized
    /// &lt;summary&gt; on the property; <c>OpenApiDocumentBuilder</c>'s doc-resolver
    /// pass picks it up and writes it to the inline schema.
    /// </summary>
    [Fact]
    public void CreatePositionalCustomerRequest_NameProperty_DescriptionFromXmlParam()
    {
        var schema = ResolveComponentSchema("CreatePositionalCustomerRequest");

        var nameProp = (OpenApiSchema)schema.Properties!["name"];
        nameProp.Description.Should().Be("Customer full name",
            because: "<param name=\"Name\"> on the positional record must populate property description");
    }

    /// <summary>
    /// <c>[Description("Internal description override")]</c> as a default-target attribute
    /// on a positional ctor parameter must reach the inline property schema's
    /// <c>description</c> via the merge — overriding any XML-derived description.
    /// </summary>
    [Fact]
    public void CreatePositionalCustomerRequest_DescriptionProperty_UsesAttributeOverride()
    {
        var schema = ResolveComponentSchema("CreatePositionalCustomerRequest");

        var descProp = (OpenApiSchema)schema.Properties!["description"];
        descProp.Description.Should().Be("Internal description override",
            because: "[Description(...)] on the positional ctor param wins over XML <param>");
    }

    /// <summary>
    /// Reflection's <c>GetConstructors()</c> ordering is unspecified. When a record
    /// has both a primary ctor (with <c>[Required, StringLength(64)] string Name</c>)
    /// and a secondary ctor with the same parameter name but no attributes, the merge
    /// must select the primary ctor — type-match tiebreak guarantees this.
    /// </summary>
    [Fact]
    public void SecondaryCtorRecord_PrimaryCtorAttributes_WinOverNoAttrSecondaryCtor()
    {
        var schema = ResolveComponentSchema("SecondaryCtorRecord");

        schema.Required.Should().NotBeNull();
        schema.Required!.Should().Contain("name",
            because: "primary ctor param has [Required] — secondary ctor param does not");

        var nameProp = (OpenApiSchema)schema.Properties!["name"];
        nameProp.MaxLength.Should().Be(64,
            because: "primary ctor param has [StringLength(64)] — secondary ctor param does not");
    }

    // =========================================================================
    // Bug #4 — [Produces("text/event-stream")] without BodyType emits Content section
    // =========================================================================

    /// <summary>
    /// <c>EventStreamController.Subscribe</c> is decorated with <c>[Produces("text/event-stream")]</c>
    /// and <c>[ProducesResponseType(StatusCodes.Status200OK)]</c> (no <c>typeof(T)</c>).
    /// The 200 response must emit a Content section with the <c>text/event-stream</c> key,
    /// even though no body schema is available.
    /// </summary>
    [Fact]
    public void EventStream_Subscribe_200Response_HasTextEventStreamContent()
    {
        var operation = FindOperation("/api/v1/events", HttpMethod.Get);

        operation.Responses.Should().ContainKey("200");
        var response = operation.Responses["200"] as OpenApiResponse
            ?? throw new InvalidOperationException("200 response is not OpenApiResponse.");

        response.Content.Should().NotBeNull(
            because: "[Produces(\"text/event-stream\")] must emit a Content section even with no body type");
        response.Content.Should().ContainKey("text/event-stream",
            because: "the explicit [Produces] content type must appear as a Content key");
    }

    [Fact]
    public void EventStream_Subscribe_200Response_TextEventStream_HasNoSchema()
    {
        var operation = FindOperation("/api/v1/events", HttpMethod.Get);

        var response = (operation.Responses?["200"] as OpenApiResponse)
            ?? throw new InvalidOperationException("200 response is not OpenApiResponse.");
        var mediaType = response.Content!["text/event-stream"] as OpenApiMediaType
            ?? throw new InvalidOperationException("text/event-stream entry is not OpenApiMediaType.");

        mediaType.Schema.Should().BeNull(
            because: "no body type was declared — the media type entry should have no schema");
    }

    // =========================================================================
    // Bug #3 — $ref properties carry description and constraints via allOf wrapping
    // =========================================================================

    /// <summary>
    /// <c>OuterWithRefPropertyModel.Inner</c> is typed as <c>InnerDto?</c> (nullable).
    /// After MakeNullable wraps it in anyOf, the enclosing schema is mutable and can
    /// carry a description. The description from <c>[Description]</c> must be present.
    /// </summary>
    [Fact]
    public void OuterWithRefPropertyModel_Inner_HasDescription()
    {
        var schema = ResolveComponentSchema("OuterWithRefPropertyModel");
        schema.Properties.Should().ContainKey("inner");

        var innerProp = schema.Properties!["inner"];
        var description = innerProp switch
        {
            OpenApiSchema s => s.Description,
            _ => null,
        };

        description.Should().Be("Inner reference description",
            because: "[Description] attribute on a $ref property must propagate verbatim through the allOf wrapper");
    }

    /// <summary>
    /// <c>OuterWithRefPropertyModel.RequiredInner</c> is typed as non-nullable <c>InnerDto</c>
    /// (not wrapped by MakeNullable). It must be wrapped in allOf with a description because
    /// it carries <c>[Description]</c> and its schema is a bare <c>$ref</c>.
    /// </summary>
    [Fact]
    public void OuterWithRefPropertyModel_RequiredInner_HasDescriptionViaAllOf()
    {
        var schema = ResolveComponentSchema("OuterWithRefPropertyModel");
        schema.Properties.Should().ContainKey("requiredInner");

        var innerProp = schema.Properties!["requiredInner"] as OpenApiSchema;
        innerProp.Should().NotBeNull(
            because: "a $ref property with [Description] must be wrapped in allOf (OpenApiSchema), not remain a bare OpenApiSchemaReference");

        innerProp!.AllOf.Should().NotBeNullOrEmpty(
            because: "the allOf wrapper must contain the $ref to InnerDto");

        innerProp.Description.Should().Be("Non-nullable inner ref description",
            because: "the [Description] attribute must appear on the allOf wrapper");
    }

    /// <summary>
    /// <c>OuterWithRefPropertyModel.XmlOnlyRef</c> is a non-nullable <c>InnerDto</c> property
    /// with only an XML <c>&lt;summary&gt;</c> (no <c>[Description]</c> attribute).
    /// The <see cref="OpenApiDocumentBuilder"/> XML-doc loop at the schema-enrichment phase
    /// must detect that the property schema is a bare <c>$ref</c>, wrap it in allOf, and attach
    /// the description — exercising the Builder path independently of SchemaGenerator.
    /// </summary>
    [Fact]
    public void OuterWithRefPropertyModel_XmlOnlyRef_HasDescriptionFromXmlSummaryViaBuilder()
    {
        var schema = ResolveComponentSchema("OuterWithRefPropertyModel");
        schema.Properties.Should().ContainKey("xmlOnlyRef");

        var prop = schema.Properties!["xmlOnlyRef"] as OpenApiSchema;
        prop.Should().NotBeNull(
            because: "a bare $ref property whose description comes from XML <summary> must be wrapped in allOf by the Builder");

        prop!.AllOf.Should().NotBeNullOrEmpty(
            because: "the allOf wrapper must contain the $ref to InnerDto");

        prop.Description.Should().Be("XML-only description on a non-nullable $ref property — no [Description] attribute.",
            because: "the XML <summary> text must propagate verbatim to the allOf wrapper schema description");
    }
}
