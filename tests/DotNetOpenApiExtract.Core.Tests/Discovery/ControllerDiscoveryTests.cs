using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Discovery;

public class ControllerDiscoveryTests : IDisposable
{
    private static readonly string SampleApiDll = TestPaths.SampleApiDll;

    private readonly AssemblyLoader _loader;
    private readonly IReadOnlyList<ControllerInfo> _controllers;

    public ControllerDiscoveryTests()
    {
        _loader = new AssemblyLoader(SampleApiDll);
        _controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    // -------------------------------------------------------------------------
    // Inclusion tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverControllers_IncludesUsersController()
    {
        _controllers.Should().Contain(c => c.Name == "Users");
    }

    [Fact]
    public void DiscoverControllers_IncludesHealthController()
    {
        _controllers.Should().Contain(c => c.Name == "Health");
    }

    [Fact]
    public void DiscoverControllers_IncludesOrdersController()
    {
        _controllers.Should().Contain(c => c.Name == "Orders");
    }

    [Fact]
    public void DiscoverControllers_IncludesProductsController()
    {
        _controllers.Should().Contain(c => c.Name == "Products");
    }

    // -------------------------------------------------------------------------
    // Exclusion tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverControllers_ExcludesInternalController_ApiExplorerSettingsIgnoreApi()
    {
        // InternalController has [ApiExplorerSettings(IgnoreApi = true)]
        _controllers.Should().NotContain(c => c.Name == "Internal");
    }

    [Fact]
    public void DiscoverControllers_ExcludesAbstractBaseController_AbstractClass()
    {
        // AbstractBaseController is abstract
        _controllers.Should().NotContain(c => c.Name == "AbstractBase");
    }

    [Fact]
    public void DiscoverControllers_ExcludesNonApiController_NonControllerAttribute()
    {
        // NonApiController has [NonController]
        _controllers.Should().NotContain(c => c.Name == "NonApi");
    }

    // -------------------------------------------------------------------------
    // Count test
    // -------------------------------------------------------------------------

    [Fact]
    public void DiscoverControllers_ReturnsExpectedCount()
    {
        // Users, Health, Orders, Products, Files, Deprecated, ObsoleteDto (7)
        // + Versioned, Status, VersioningUnion, VersioningDedup, VersioningActionNeutral,
        //   VersioningIntConstructor (6 versioning test controllers added for T10)
        // + VersioningStatusSuffix, VersioningDoubleStatusSuffix (2 status-suffix controllers added for W1)
        // + VersioningBareDouble, VersioningNeutralOverridesVersion (2 audit gap fixtures)
        // + Secure (1 security test controller added for T3)
        // + JsonConverter (1 converter schema test controller added for T6)
        // + RateLimiting, Caching (2 rate-limiting/caching controllers added for T13)
        // + ValidationModel (1 controller surfacing ValidationModel/ExtendedValidationModel for schema.property-constraints integration tests)
        // + NestedDto, Inheritance (2 controllers for nested-type and inheritance XML doc bug fixes)
        // + EventStream (1 SSE content-type fixture for Bug #4 fix)
        // + RefProperty (1 $ref-siblings fixture for Bug #3 fix)
        // + FrameworkTypeRef (1 controller for framework XML doc loading test)
        _controllers.Should().HaveCount(27);
    }

    // -------------------------------------------------------------------------
    // UsersController metadata
    // -------------------------------------------------------------------------

    [Fact]
    public void UsersController_HasCorrectName()
    {
        var users = _controllers.Single(c => c.Name == "Users");
        users.Name.Should().Be("Users");
    }

    [Fact]
    public void UsersController_HasCorrectRouteTemplate()
    {
        var users = _controllers.Single(c => c.Name == "Users");
        // [Route("api/v1/[controller]")]
        users.RouteTemplate.Should().Be("api/v1/[controller]");
    }

    [Fact]
    public void UsersController_TagDescriptionContainsCrud()
    {
        var users = _controllers.Single(c => c.Name == "Users");
        // [SwaggerTag("User management — CRUD operations for users")]
        users.TagDescription.Should().NotBeNull();
        users.TagDescription.Should().Contain("CRUD");
    }

    [Fact]
    public void UsersController_GroupNameIsNull()
    {
        var users = _controllers.Single(c => c.Name == "Users");
        // No [ApiExplorerSettings(GroupName = "...")] on UsersController
        users.GroupName.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // HealthController metadata
    // -------------------------------------------------------------------------

    [Fact]
    public void HealthController_HasCorrectName()
    {
        var health = _controllers.Single(c => c.Name == "Health");
        health.Name.Should().Be("Health");
    }

    [Fact]
    public void HealthController_HasCorrectRouteTemplate()
    {
        var health = _controllers.Single(c => c.Name == "Health");
        // [Route("[controller]")]
        health.RouteTemplate.Should().Be("[controller]");
    }

    [Fact]
    public void HealthController_GroupNameIsNull()
    {
        var health = _controllers.Single(c => c.Name == "Health");
        health.GroupName.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // OrdersController metadata
    // -------------------------------------------------------------------------

    [Fact]
    public void OrdersController_HasCorrectName()
    {
        var orders = _controllers.Single(c => c.Name == "Orders");
        orders.Name.Should().Be("Orders");
    }

    [Fact]
    public void OrdersController_HasCorrectRouteTemplate()
    {
        var orders = _controllers.Single(c => c.Name == "Orders");
        // [Route("api/v1/orders")]
        orders.RouteTemplate.Should().Be("api/v1/orders");
    }

    [Fact]
    public void OrdersController_GroupNameIsNull()
    {
        var orders = _controllers.Single(c => c.Name == "Orders");
        orders.GroupName.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // ProductsController metadata
    // -------------------------------------------------------------------------

    [Fact]
    public void ProductsController_HasCorrectName()
    {
        var products = _controllers.Single(c => c.Name == "Products");
        products.Name.Should().Be("Products");
    }

    [Fact]
    public void ProductsController_RouteTemplateContainsActionToken()
    {
        var products = _controllers.Single(c => c.Name == "Products");
        // [Route("api/v1/[controller]/[action]")]
        products.RouteTemplate.Should().NotBeNull();
        products.RouteTemplate.Should().Contain("[action]");
    }

    [Fact]
    public void ProductsController_GroupNameIsNull()
    {
        var products = _controllers.Single(c => c.Name == "Products");
        products.GroupName.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // Type reference integrity
    // -------------------------------------------------------------------------

    [Fact]
    public void AllDiscoveredControllers_HaveNonNullType()
    {
        foreach (var controller in _controllers)
        {
            controller.Type.Should().NotBeNull(
                because: $"controller '{controller.Name}' must have a reflected Type");
        }
    }

    [Fact]
    public void AllDiscoveredControllers_TypeFullNamesControllersNamespace()
    {
        foreach (var controller in _controllers)
        {
            controller.Type.FullName.Should().StartWith("SampleApi.Controllers.",
                because: $"controller '{controller.Name}' must live in SampleApi.Controllers");
        }
    }
}
