using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Discovery;

public class ActionDiscoveryTests : IDisposable
{
    private static readonly string SampleApiDll = TestPaths.SampleApiDll;

    private readonly AssemblyLoader _loader;
    private readonly IReadOnlyList<ControllerInfo> _controllers;

    // Per-controller action lists, resolved once in the constructor.
    private readonly IReadOnlyList<ActionInfo> _usersActions;
    private readonly IReadOnlyList<ActionInfo> _healthActions;
    private readonly IReadOnlyList<ActionInfo> _ordersActions;
    private readonly IReadOnlyList<ActionInfo> _productsActions;

    public ActionDiscoveryTests()
    {
        _loader = new AssemblyLoader(SampleApiDll);
        _controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);

        _usersActions   = ActionDiscovery.DiscoverActions(_controllers.Single(c => c.Name == "Users"));
        _healthActions  = ActionDiscovery.DiscoverActions(_controllers.Single(c => c.Name == "Health"));
        _ordersActions  = ActionDiscovery.DiscoverActions(_controllers.Single(c => c.Name == "Orders"));
        _productsActions = ActionDiscovery.DiscoverActions(_controllers.Single(c => c.Name == "Products"));
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    // -------------------------------------------------------------------------
    // UsersController — action count
    // -------------------------------------------------------------------------

    [Fact]
    public void UsersController_HasSixActions()
    {
        // GetUsers, GetUser, CreateUser, DeleteUser, UpdateUser, SearchUsers
        _usersActions.Should().HaveCount(6);
    }

    // -------------------------------------------------------------------------
    // UsersController — GetUsers
    // -------------------------------------------------------------------------

    [Fact]
    public void UsersController_GetUsers_HttpMethodIsGet()
    {
        var action = _usersActions.Single(a => a.Name == "GetUsers");
        action.HttpMethod.Should().Be("GET");
    }

    [Fact]
    public void UsersController_GetUsers_RouteTemplateIsNull()
    {
        // [HttpGet] with no argument → no route template on the action
        var action = _usersActions.Single(a => a.Name == "GetUsers");
        action.RouteTemplate.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // UsersController — GetUser
    // -------------------------------------------------------------------------

    [Fact]
    public void UsersController_GetUser_HttpMethodIsGet()
    {
        var action = _usersActions.Single(a => a.Name == "GetUser");
        action.HttpMethod.Should().Be("GET");
    }

    [Fact]
    public void UsersController_GetUser_RouteTemplateIsId()
    {
        // [HttpGet("{id}")]
        var action = _usersActions.Single(a => a.Name == "GetUser");
        action.RouteTemplate.Should().Be("{id}");
    }

    // -------------------------------------------------------------------------
    // UsersController — CreateUser
    // -------------------------------------------------------------------------

    [Fact]
    public void UsersController_CreateUser_HttpMethodIsPost()
    {
        var action = _usersActions.Single(a => a.Name == "CreateUser");
        action.HttpMethod.Should().Be("POST");
    }

    // -------------------------------------------------------------------------
    // UsersController — DeleteUser
    // -------------------------------------------------------------------------

    [Fact]
    public void UsersController_DeleteUser_HttpMethodIsDelete()
    {
        var action = _usersActions.Single(a => a.Name == "DeleteUser");
        action.HttpMethod.Should().Be("DELETE");
    }

    [Fact]
    public void UsersController_DeleteUser_RouteTemplateIsId()
    {
        // [HttpDelete("{id}")]
        var action = _usersActions.Single(a => a.Name == "DeleteUser");
        action.RouteTemplate.Should().Be("{id}");
    }

    // -------------------------------------------------------------------------
    // HealthController
    // -------------------------------------------------------------------------

    [Fact]
    public void HealthController_HasOneAction()
    {
        _healthActions.Should().HaveCount(1);
    }

    [Fact]
    public void HealthController_Healthz_HttpMethodIsGet()
    {
        var action = _healthActions.Single(a => a.Name == "Healthz");
        action.HttpMethod.Should().Be("GET");
    }

    [Fact]
    public void HealthController_Healthz_RouteTemplateIsAbsolutePath()
    {
        // [HttpGet("/healthz")] — absolute route starting with "/"
        var action = _healthActions.Single(a => a.Name == "Healthz");
        action.RouteTemplate.Should().Be("/healthz");
    }

    // -------------------------------------------------------------------------
    // OrdersController — action count and exclusions
    // -------------------------------------------------------------------------

    [Fact]
    public void OrdersController_HasExactlyThreeActions()
    {
        // GetOrder, GetOrderItem, SearchOrders (Find with [ActionName("SearchOrders")])
        // HelperMethod must NOT appear — it has [NonAction]
        _ordersActions.Should().HaveCount(3);
    }

    [Fact]
    public void OrdersController_HelperMethod_IsNotDiscovered()
    {
        _ordersActions.Should().NotContain(a => a.Name == "HelperMethod");
    }

    // -------------------------------------------------------------------------
    // OrdersController — SearchOrders ([ActionName])
    // -------------------------------------------------------------------------

    [Fact]
    public void OrdersController_FindMethod_HasActionNameSearchOrders()
    {
        // The method is named Find but [ActionName("SearchOrders")] overrides the name
        _ordersActions.Should().Contain(a => a.Name == "SearchOrders");
    }

    [Fact]
    public void OrdersController_SearchOrders_HttpMethodIsGet()
    {
        var action = _ordersActions.Single(a => a.Name == "SearchOrders");
        action.HttpMethod.Should().Be("GET");
    }

    // -------------------------------------------------------------------------
    // OrdersController — GetOrder
    // -------------------------------------------------------------------------

    [Fact]
    public void OrdersController_GetOrder_RouteTemplateHasGuidConstraint()
    {
        // [HttpGet("{id:guid}")]
        var action = _ordersActions.Single(a => a.Name == "GetOrder");
        action.RouteTemplate.Should().Be("{id:guid}");
    }

    [Fact]
    public void OrdersController_GetOrder_HttpMethodIsGet()
    {
        var action = _ordersActions.Single(a => a.Name == "GetOrder");
        action.HttpMethod.Should().Be("GET");
    }

    // -------------------------------------------------------------------------
    // OrdersController — GetOrderItem
    // -------------------------------------------------------------------------

    [Fact]
    public void OrdersController_GetOrderItem_HttpMethodIsGet()
    {
        var action = _ordersActions.Single(a => a.Name == "GetOrderItem");
        action.HttpMethod.Should().Be("GET");
    }

    [Fact]
    public void OrdersController_GetOrderItem_RouteTemplateHasMultipleSegments()
    {
        // [HttpGet("{orderId:guid}/items/{itemId:int}")]
        var action = _ordersActions.Single(a => a.Name == "GetOrderItem");
        action.RouteTemplate.Should().Be("{orderId:guid}/items/{itemId:int}");
    }

    // -------------------------------------------------------------------------
    // ProductsController — Featured absolute route
    // -------------------------------------------------------------------------

    [Fact]
    public void ProductsController_Featured_RouteTemplateStartsWithSlash()
    {
        // [HttpGet("/api/v1/catalog/featured")] — absolute route
        var action = _productsActions.Single(a => a.Name == "Featured");
        action.RouteTemplate.Should().NotBeNull();
        action.RouteTemplate.Should().StartWith("/");
    }

    [Fact]
    public void ProductsController_Featured_HttpMethodIsGet()
    {
        var action = _productsActions.Single(a => a.Name == "Featured");
        action.HttpMethod.Should().Be("GET");
    }

    // -------------------------------------------------------------------------
    // ProductsController — List (no route argument on [HttpGet])
    // -------------------------------------------------------------------------

    [Fact]
    public void ProductsController_List_RouteTemplateIsNull()
    {
        // [HttpGet] with no argument
        var action = _productsActions.Single(a => a.Name == "List");
        action.RouteTemplate.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // ProductsController — Details route with constraint
    // -------------------------------------------------------------------------

    [Fact]
    public void ProductsController_Details_RouteTemplateHasIntConstraint()
    {
        // [HttpGet("{id:int}")]
        var action = _productsActions.Single(a => a.Name == "Details");
        action.RouteTemplate.Should().Be("{id:int}");
    }

    // -------------------------------------------------------------------------
    // Total action count across all controllers
    // -------------------------------------------------------------------------

    [Fact]
    public void AllControllers_TotalActionCountIsCorrect()
    {
        // Non-versioning: Users=6, Health=1, Orders=3, Products=4, Files=5, Deprecated=2, ObsoleteDto=1 — 22
        // Versioning (actions each): Versioned=2, Status=1, VersioningUnion=1, VersioningDedup=1,
        //                            VersioningActionNeutral=1, VersioningIntConstructor=1,
        //                            VersioningStatusSuffix=1, VersioningDoubleStatusSuffix=1 — 9
        // Audit gap fixtures: VersioningBareDouble=1, VersioningNeutralOverridesVersion=1 — 2
        // Security: Secure=3 — 3
        // JsonConverter: JsonConverter=1 — 1
        // RateLimiting (T13): RateLimiting=4 — 4 (added GetDisableWins for Disable+Enable precedence test)
        // Caching (T13): Caching=6 — 6 (added GetNoStoreWithDuration, GetLocationNone)
        // ValidationModel: Echo + EchoExtended + EchoPositionalCustomer + EchoSecondaryCtorRecord — 4
        //                  (the latter two surface positional-record fixtures for the
        //                   primary-ctor parameter-attribute merge fix)
        // NestedDto (Bug A fixture): GetService + CreateService — 2
        // Inheritance (Bug B fixture): CreateServer — 1
        // Total: 54
        var allActions = ActionDiscovery.DiscoverActions(_controllers);
        allActions.Should().HaveCount(54);
    }

    // -------------------------------------------------------------------------
    // ActionInfo back-reference to controller
    // -------------------------------------------------------------------------

    [Fact]
    public void AllActions_HaveControllerBackReference()
    {
        var allActions = ActionDiscovery.DiscoverActions(_controllers);
        foreach (var action in allActions)
        {
            action.Controller.Should().NotBeNull(
                because: $"action '{action.Name}' must reference its owning controller");
        }
    }

    [Fact]
    public void UsersActions_AllReferenceUsersController()
    {
        foreach (var action in _usersActions)
        {
            action.Controller.Name.Should().Be("Users",
                because: $"action '{action.Name}' belongs to UsersController");
        }
    }
}
