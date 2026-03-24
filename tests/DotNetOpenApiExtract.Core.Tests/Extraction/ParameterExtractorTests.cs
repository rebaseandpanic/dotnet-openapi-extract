using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="ParameterExtractor"/>. Tests load SampleApi.dll via
/// <see cref="AssemblyLoader"/>, discover controllers and actions, then exercise
/// <see cref="ParameterExtractor.ExtractParameters"/> against specific actions.
/// </summary>
public class ParameterExtractorTests : IDisposable
{
    private readonly AssemblyLoader _loader;

    // Pre-resolved actions used across multiple tests.
    private readonly ActionInfo _getUsersAction;
    private readonly ActionInfo _getUserAction;
    private readonly ActionInfo _createUserAction;
    private readonly ActionInfo _deleteUserAction;
    private readonly ActionInfo _uploadAction;
    private readonly ActionInfo _downloadAction;
    private readonly ActionInfo _findOrdersAction;
    private readonly ActionInfo _getOrderItemAction;

    public ParameterExtractorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        var controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);

        var usersController   = controllers.Single(c => c.Name == "Users");
        var filesController   = controllers.Single(c => c.Name == "Files");
        var ordersController  = controllers.Single(c => c.Name == "Orders");

        var usersActions  = ActionDiscovery.DiscoverActions(usersController);
        var filesActions  = ActionDiscovery.DiscoverActions(filesController);
        var ordersActions = ActionDiscovery.DiscoverActions(ordersController);

        _getUsersAction    = usersActions.Single(a => a.Name == "GetUsers");
        _getUserAction     = usersActions.Single(a => a.Name == "GetUser");
        _createUserAction  = usersActions.Single(a => a.Name == "CreateUser");
        _deleteUserAction  = usersActions.Single(a => a.Name == "DeleteUser");
        _uploadAction      = filesActions.Single(a => a.Name == "Upload");
        _downloadAction    = filesActions.Single(a => a.Name == "Download");
        _findOrdersAction  = ordersActions.Single(a => a.Name == "SearchOrders");
        _getOrderItemAction = ordersActions.Single(a => a.Name == "GetOrderItem");
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    // -------------------------------------------------------------------------
    // UsersController.GetUsers — query parameters with defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void GetUsers_HasThreeParameters()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        parameters.Should().HaveCount(3);
    }

    [Fact]
    public void GetUsers_StatusParam_IsQueryLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var status = parameters.Single(p => p.Name == "status");
        status.Location.Should().Be(ParameterLocation.Query);
    }

    [Fact]
    public void GetUsers_StatusParam_IsNullableEnum()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var status = parameters.Single(p => p.Name == "status");

        // The type is Nullable<UserStatus> (i.e. UserStatus?)
        status.Type.IsGenericType.Should().BeTrue();
        status.Type.GetGenericTypeDefinition().FullName.Should().Be("System.Nullable`1");

        var underlyingType = status.Type.GetGenericArguments()[0];
        underlyingType.IsEnum.Should().BeTrue();
        underlyingType.Name.Should().Be("UserStatus");
    }

    [Fact]
    public void GetUsers_StatusParam_IsNotRequired()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var status = parameters.Single(p => p.Name == "status");
        status.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void GetUsers_PageParam_IsQueryLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var page = parameters.Single(p => p.Name == "page");
        page.Location.Should().Be(ParameterLocation.Query);
    }

    [Fact]
    public void GetUsers_PageParam_TypeIsInt()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var page = parameters.Single(p => p.Name == "page");
        page.Type.FullName.Should().Be("System.Int32");
    }

    [Fact]
    public void GetUsers_PageParam_IsNotRequired()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var page = parameters.Single(p => p.Name == "page");
        page.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void GetUsers_PageParam_DefaultValueIsOne()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var page = parameters.Single(p => p.Name == "page");
        page.DefaultValue.Should().Be(1);
    }

    [Fact]
    public void GetUsers_PageSizeParam_IsQueryLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var pageSize = parameters.Single(p => p.Name == "pageSize");
        pageSize.Location.Should().Be(ParameterLocation.Query);
    }

    [Fact]
    public void GetUsers_PageSizeParam_TypeIsInt()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var pageSize = parameters.Single(p => p.Name == "pageSize");
        pageSize.Type.FullName.Should().Be("System.Int32");
    }

    [Fact]
    public void GetUsers_PageSizeParam_IsNotRequired()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var pageSize = parameters.Single(p => p.Name == "pageSize");
        pageSize.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void GetUsers_PageSizeParam_DefaultValueIsTwenty()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var pageSize = parameters.Single(p => p.Name == "pageSize");
        pageSize.DefaultValue.Should().Be(20);
    }

    [Fact]
    public void GetUsers_PageParam_HasSwaggerParameterDescription()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUsersAction);
        var page = parameters.Single(p => p.Name == "page");
        page.Description.Should().Be("Page number (1-based)");
    }

    // -------------------------------------------------------------------------
    // UsersController.GetUser — path parameter
    // -------------------------------------------------------------------------

    [Fact]
    public void GetUser_HasOneParameter()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUserAction);
        parameters.Should().HaveCount(1);
    }

    [Fact]
    public void GetUser_IdParam_IsPathLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUserAction);
        var id = parameters.Single(p => p.Name == "id");
        id.Location.Should().Be(ParameterLocation.Path);
    }

    [Fact]
    public void GetUser_IdParam_TypeIsGuid()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUserAction);
        var id = parameters.Single(p => p.Name == "id");
        id.Type.FullName.Should().Be("System.Guid");
    }

    [Fact]
    public void GetUser_IdParam_IsRequired()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUserAction);
        var id = parameters.Single(p => p.Name == "id");
        id.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void GetUser_IdParam_HasSwaggerParameterDescription()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getUserAction);
        var id = parameters.Single(p => p.Name == "id");
        id.Description.Should().Be("User unique identifier");
    }

    // -------------------------------------------------------------------------
    // UsersController.CreateUser — body parameter
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateUser_HasOneParameter()
    {
        var parameters = ParameterExtractor.ExtractParameters(_createUserAction);
        parameters.Should().HaveCount(1);
    }

    [Fact]
    public void CreateUser_RequestParam_IsBodyLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_createUserAction);
        var request = parameters.Single(p => p.Name == "request");
        request.Location.Should().Be(ParameterLocation.Body);
    }

    [Fact]
    public void CreateUser_RequestParam_TypeIsCreateUserRequest()
    {
        var parameters = ParameterExtractor.ExtractParameters(_createUserAction);
        var request = parameters.Single(p => p.Name == "request");
        request.Type.Name.Should().Be("CreateUserRequest");
    }

    // -------------------------------------------------------------------------
    // UsersController.DeleteUser — path parameter
    // -------------------------------------------------------------------------

    [Fact]
    public void DeleteUser_IdParam_IsPathLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_deleteUserAction);
        var id = parameters.Single(p => p.Name == "id");
        id.Location.Should().Be(ParameterLocation.Path);
    }

    [Fact]
    public void DeleteUser_IdParam_IsRequired()
    {
        var parameters = ParameterExtractor.ExtractParameters(_deleteUserAction);
        var id = parameters.Single(p => p.Name == "id");
        id.IsRequired.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // FilesController.Upload — IFormFile + mixed sources + CancellationToken skip
    // -------------------------------------------------------------------------

    [Fact]
    public void Upload_HasThreeParameters()
    {
        // file, category, overwrite — CancellationToken (ct) must be excluded
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        parameters.Should().HaveCount(3);
    }

    [Fact]
    public void Upload_CancellationToken_IsNotExtracted()
    {
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        parameters.Should().NotContain(p => p.Name == "ct");
    }

    [Fact]
    public void Upload_FileParam_IsPresent()
    {
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        parameters.Should().Contain(p => p.Name == "file");
    }

    [Fact]
    public void Upload_FileParam_TypeIsIFormFile()
    {
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        var file = parameters.Single(p => p.Name == "file");
        file.Type.Name.Should().Be("IFormFile");
    }

    [Fact]
    public void Upload_CategoryParam_IsQueryLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        var category = parameters.Single(p => p.Name == "category");
        category.Location.Should().Be(ParameterLocation.Query);
    }

    [Fact]
    public void Upload_CategoryParam_DefaultValueIsGeneral()
    {
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        var category = parameters.Single(p => p.Name == "category");
        category.DefaultValue.Should().Be("general");
    }

    [Fact]
    public void Upload_OverwriteParam_IsQueryLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        var overwrite = parameters.Single(p => p.Name == "overwrite");
        overwrite.Location.Should().Be(ParameterLocation.Query);
    }

    [Fact]
    public void Upload_OverwriteParam_DefaultValueIsFalse()
    {
        var parameters = ParameterExtractor.ExtractParameters(_uploadAction);
        var overwrite = parameters.Single(p => p.Name == "overwrite");
        overwrite.DefaultValue.Should().Be(false);
    }

    // -------------------------------------------------------------------------
    // FilesController.Download — header parameter with Name override
    // -------------------------------------------------------------------------

    [Fact]
    public void Download_IdParam_IsPathLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_downloadAction);
        var id = parameters.Single(p => p.Name == "id");
        id.Location.Should().Be(ParameterLocation.Path);
    }

    [Fact]
    public void Download_ApiKeyParam_IsHeaderLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_downloadAction);
        // Name is overridden by [FromHeader(Name = "X-Api-Key")]
        var apiKey = parameters.Single(p => p.Name == "X-Api-Key");
        apiKey.Location.Should().Be(ParameterLocation.Header);
    }

    [Fact]
    public void Download_ApiKeyParam_NameIsFromHeaderNameProperty()
    {
        // [FromHeader(Name = "X-Api-Key")] overrides the C# parameter name "apiKey"
        var parameters = ParameterExtractor.ExtractParameters(_downloadAction);
        parameters.Should().Contain(p => p.Name == "X-Api-Key");
        parameters.Should().NotContain(p => p.Name == "apiKey");
    }

    [Fact]
    public void Download_ApiKeyParam_HasSwaggerParameterDescription()
    {
        var parameters = ParameterExtractor.ExtractParameters(_downloadAction);
        var apiKey = parameters.Single(p => p.Name == "X-Api-Key");
        apiKey.Description.Should().Be("API key for authentication");
    }

    // -------------------------------------------------------------------------
    // OrdersController.Find — query parameters, nullable types
    // -------------------------------------------------------------------------

    [Fact]
    public void FindOrders_QueryParam_IsQueryLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_findOrdersAction);
        var query = parameters.Single(p => p.Name == "query");
        query.Location.Should().Be(ParameterLocation.Query);
    }

    [Fact]
    public void FindOrders_QueryParam_IsNotRequired_BecauseNullableString()
    {
        // string? is a nullable reference type → IsRequired = false
        var parameters = ParameterExtractor.ExtractParameters(_findOrdersAction);
        var query = parameters.Single(p => p.Name == "query");
        query.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void FindOrders_FromDateParam_IsQueryLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_findOrdersAction);
        var fromDate = parameters.Single(p => p.Name == "fromDate");
        fromDate.Location.Should().Be(ParameterLocation.Query);
    }

    [Fact]
    public void FindOrders_FromDateParam_IsNotRequired_BecauseNullableDateTimeOffset()
    {
        // DateTimeOffset? is Nullable<DateTimeOffset> → IsRequired = false
        var parameters = ParameterExtractor.ExtractParameters(_findOrdersAction);
        var fromDate = parameters.Single(p => p.Name == "fromDate");
        fromDate.IsRequired.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // OrdersController.GetOrderItem — multiple path parameters
    // -------------------------------------------------------------------------

    [Fact]
    public void GetOrderItem_HasTwoParameters()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getOrderItemAction);
        parameters.Should().HaveCount(2);
    }

    [Fact]
    public void GetOrderItem_OrderIdParam_IsPathLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getOrderItemAction);
        var orderId = parameters.Single(p => p.Name == "orderId");
        orderId.Location.Should().Be(ParameterLocation.Path);
    }

    [Fact]
    public void GetOrderItem_ItemIdParam_IsPathLocation()
    {
        var parameters = ParameterExtractor.ExtractParameters(_getOrderItemAction);
        var itemId = parameters.Single(p => p.Name == "itemId");
        itemId.Location.Should().Be(ParameterLocation.Path);
    }

    [Fact]
    public void GetOrderItem_BothParams_AreRequired()
    {
        // Path parameters are unconditionally required by OpenAPI specification.
        var parameters = ParameterExtractor.ExtractParameters(_getOrderItemAction);
        parameters.Should().AllSatisfy(p => p.IsRequired.Should().BeTrue());
    }
}
