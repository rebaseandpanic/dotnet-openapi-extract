using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Discovery;

/// <summary>
/// Unit tests for <see cref="RouteBuilder.BuildPath"/>.
/// All tests are pure — no assembly loading required.
/// </summary>
public class RouteBuilderTests
{
    // -------------------------------------------------------------------------
    // Basic combination
    // -------------------------------------------------------------------------

    [Fact]
    public void BasicCombination_ControllerRouteOnly_ReturnsControllerSegmentWithNoTrailingSlash()
    {
        // "api/v1/[controller]" + null action, controllerName = "UsersController"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/v1/[controller]",
            actionRoute: null,
            controllerName: "UsersController",
            actionName: "Index");

        result.Should().Be("/api/v1/users");
    }

    [Fact]
    public void BasicCombination_ControllerRouteAndActionTemplate_CombinesCorrectly()
    {
        // "api/v1/[controller]" + "{id}", controllerName = "UsersController"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/v1/[controller]",
            actionRoute: "{id}",
            controllerName: "UsersController",
            actionName: "GetById");

        result.Should().Be("/api/v1/users/{id}");
    }

    [Fact]
    public void BasicCombination_ActionConstraintStripped_StaticControllerRoute()
    {
        // "api/v1/orders" + "{id:guid}"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/v1/orders",
            actionRoute: "{id:guid}",
            controllerName: "OrdersController",
            actionName: "GetById");

        result.Should().Be("/api/v1/orders/{id}");
    }

    [Fact]
    public void BasicCombination_NullControllerRoute_ActionOnly()
    {
        // null controller + "healthz"
        string result = RouteBuilder.BuildPath(
            controllerRoute: null,
            actionRoute: "healthz",
            controllerName: "HealthController",
            actionName: "Check");

        result.Should().Be("/healthz");
    }

    // -------------------------------------------------------------------------
    // Token replacement
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenReplacement_ControllerToken_ReplacedWithControllerName()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "[controller]",
            actionRoute: null,
            controllerName: "UsersController",
            actionName: "Index");

        result.Should().Be("/users");
    }

    [Fact]
    public void TokenReplacement_ActionToken_ReplacedWithActionName()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: null,
            actionRoute: "[action]",
            controllerName: "UsersController",
            actionName: "GetUser");

        result.Should().Be("/getuser");
    }

    [Fact]
    public void TokenReplacement_BothTokensInControllerRoute_BothReplaced()
    {
        // "api/v1/[controller]/[action]", controllerName="ProductsController", actionName="List"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/v1/[controller]/[action]",
            actionRoute: null,
            controllerName: "ProductsController",
            actionName: "List");

        result.Should().Be("/api/v1/products/list");
    }

    [Fact]
    public void TokenReplacement_BothTokensWithParameterSegment()
    {
        // "[controller]/[action]/{id}", controllerName="ProductsController", actionName="Details"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "[controller]/[action]/{id}",
            actionRoute: null,
            controllerName: "ProductsController",
            actionName: "Details");

        result.Should().Be("/products/details/{id}");
    }

    // -------------------------------------------------------------------------
    // Absolute routes
    // -------------------------------------------------------------------------

    [Fact]
    public void AbsoluteRoute_ActionStartsWithSlash_ControllerRouteIgnored()
    {
        // controller = "api/v1/[controller]", action = "/api/v1/catalog/featured"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/v1/[controller]",
            actionRoute: "/api/v1/catalog/featured",
            controllerName: "CatalogController",
            actionName: "Featured");

        result.Should().Be("/api/v1/catalog/featured");
    }

    [Fact]
    public void AbsoluteRoute_ActionStartsWithTilde_ControllerRouteIgnored()
    {
        // controller = "api/v1/[controller]", action = "~/custom/path"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/v1/[controller]",
            actionRoute: "~/custom/path",
            controllerName: "SomeController",
            actionName: "Custom");

        result.Should().Be("/custom/path");
    }

    // -------------------------------------------------------------------------
    // Route constraints
    // -------------------------------------------------------------------------

    [Fact]
    public void Constraints_IntConstraint_Stripped()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "items",
            actionRoute: "{id:int}",
            controllerName: "ItemsController",
            actionName: "Get");

        result.Should().Be("/items/{id}");
    }

    [Fact]
    public void Constraints_GuidConstraint_Stripped()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "items",
            actionRoute: "{id:guid}",
            controllerName: "ItemsController",
            actionName: "Get");

        result.Should().Be("/items/{id}");
    }

    [Fact]
    public void Constraints_MultipleParametersWithConstraints_AllStripped()
    {
        // "{orderId:guid}/items/{itemId:int}"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api",
            actionRoute: "{orderId:guid}/items/{itemId:int}",
            controllerName: "OrdersController",
            actionName: "GetItem");

        result.Should().Be("/api/{orderId}/items/{itemId}");
    }

    [Fact]
    public void Constraints_MultipleConstraintsOnOneParameter_AllStripped()
    {
        // "{id:int:min(1)}"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "items",
            actionRoute: "{id:int:min(1)}",
            controllerName: "ItemsController",
            actionName: "Get");

        result.Should().Be("/items/{id}");
    }

    // -------------------------------------------------------------------------
    // Catch-all parameters
    // -------------------------------------------------------------------------

    [Fact]
    public void CatchAll_SingleAsterisk_Normalised()
    {
        // "{*slug}" → "{slug}"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "files",
            actionRoute: "{*slug}",
            controllerName: "FilesController",
            actionName: "Get");

        result.Should().Be("/files/{slug}");
    }

    [Fact]
    public void CatchAll_DoubleAsterisk_Normalised()
    {
        // "{**path}" → "{path}"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "files",
            actionRoute: "{**path}",
            controllerName: "FilesController",
            actionName: "Get");

        result.Should().Be("/files/{path}");
    }

    // -------------------------------------------------------------------------
    // Path formatting
    // -------------------------------------------------------------------------

    [Fact]
    public void Formatting_ResultAlwaysStartsWithSlash()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/items",
            actionRoute: null,
            controllerName: "ItemsController",
            actionName: "Index");

        result.Should().StartWith("/");
    }

    [Fact]
    public void Formatting_NoTrailingSlash()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/items/",
            actionRoute: null,
            controllerName: "ItemsController",
            actionName: "Index");

        result.Should().NotEndWith("/");
    }

    [Fact]
    public void Formatting_DoubleSlashesCollapsed()
    {
        // "api/" + "/items" — both present, so joined path would produce "api//items"
        // (action does NOT start with '/' in combination context — note: this is
        // achieved by putting the double-slash in the controller route itself)
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api//v1",
            actionRoute: "items",
            controllerName: "ItemsController",
            actionName: "Index");

        result.Should().Be("/api/v1/items");
        result.Should().NotContain("//");
    }

    [Fact]
    public void Formatting_StaticSegmentsLowercased()
    {
        // "API/V1/Users" should become "/api/v1/users"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "API/V1/Users",
            actionRoute: null,
            controllerName: "SomeController",
            actionName: "Index");

        result.Should().Be("/api/v1/users");
    }

    [Fact]
    public void Formatting_ParameterNamesPreserveCase()
    {
        // "{UserId}" should stay as "{UserId}", not be lowercased
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api",
            actionRoute: "{UserId}",
            controllerName: "UsersController",
            actionName: "Get");

        result.Should().Be("/api/{UserId}");
    }

    // -------------------------------------------------------------------------
    // Edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void EdgeCase_BothRoutesNull_ReturnsRoot()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: null,
            actionRoute: null,
            controllerName: "HomeController",
            actionName: "Index");

        result.Should().Be("/");
    }

    [Fact]
    public void EdgeCase_EmptyStringControllerRoute_TreatedAsNull()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "",
            actionRoute: "healthz",
            controllerName: "HealthController",
            actionName: "Check");

        result.Should().Be("/healthz");
    }

    [Fact]
    public void EdgeCase_EmptyStringActionRoute_TreatedAsNull()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/items",
            actionRoute: "",
            controllerName: "ItemsController",
            actionName: "Index");

        result.Should().Be("/api/items");
    }

    [Fact]
    public void EdgeCase_WhitespaceOnlyRoutes_TreatedAsNull()
    {
        string result = RouteBuilder.BuildPath(
            controllerRoute: "   ",
            actionRoute: "   ",
            controllerName: "HomeController",
            actionName: "Index");

        result.Should().Be("/");
    }

    [Fact]
    public void EdgeCase_AbsoluteActionWithControllerPresent_ControllerIgnored()
    {
        // controller = "api/", action absolute "/items" — controller is ignored
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/",
            actionRoute: "/items",
            controllerName: "ItemsController",
            actionName: "GetAll");

        result.Should().Be("/items");
    }

    [Fact]
    public void EdgeCase_ComplexRouteWithMultipleConstraintsAndTokens()
    {
        // "api/v1/[controller]" + "{orderId:guid}/items/{itemId:int:min(0)}"
        // controllerName = "OrdersController"
        string result = RouteBuilder.BuildPath(
            controllerRoute: "api/v1/[controller]",
            actionRoute: "{orderId:guid}/items/{itemId:int:min(0)}",
            controllerName: "OrdersController",
            actionName: "GetItem");

        result.Should().Be("/api/v1/orders/{orderId}/items/{itemId}");
    }
}
