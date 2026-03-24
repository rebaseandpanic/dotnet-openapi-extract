using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Loading;

public class AttributeHelperTests : IDisposable
{
    private static readonly string SampleApiDll = TestPaths.SampleApiDll;

    private readonly AssemblyLoader _loader;

    public AttributeHelperTests()
    {
        _loader = new AssemblyLoader(SampleApiDll);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    // --- HasAttribute (MemberInfo) ---

    [Fact]
    public void HasAttribute_ReturnsTrueForExistingAttribute()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        AttributeHelper.HasAttribute(controller, AttributeHelper.Names.ApiController).Should().BeTrue();
    }

    [Fact]
    public void HasAttribute_ReturnsFalseForMissingAttribute()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        AttributeHelper.HasAttribute(controller, AttributeHelper.Names.NonController).Should().BeFalse();
    }

    // --- GetAttribute (MemberInfo) ---

    [Fact]
    public void GetAttribute_ReturnsAttributeData()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var attr = AttributeHelper.GetAttribute(controller, AttributeHelper.Names.Route);
        attr.Should().NotBeNull();
        attr!.ConstructorArguments[0].Value.Should().Be("api/v1/[controller]");
    }

    [Fact]
    public void GetAttribute_ReturnsNullForMissingAttribute()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var attr = AttributeHelper.GetAttribute(controller, AttributeHelper.Names.NonController);
        attr.Should().BeNull();
    }

    // --- GetAttributes (MemberInfo, multiple) ---

    [Fact]
    public void GetAttributes_ReturnsMultipleProducesResponseType()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var createUser = controller.GetMethods().First(m => m.Name == "CreateUser");

        var attrs = AttributeHelper.GetAttributes(createUser, AttributeHelper.Names.ProducesResponseType).ToList();
        // CreateUser has 3 ProducesResponseType: 201, 400, 422
        attrs.Should().HaveCount(3);
    }

    // --- GetAttribute with multiple name options ---

    [Fact]
    public void GetAttribute_MatchesAnyOfGivenNames()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUsers = controller.GetMethods().First(m => m.Name == "GetUsers");

        var attr = AttributeHelper.GetAttribute(getUsers,
            AttributeHelper.Names.HttpGet,
            AttributeHelper.Names.HttpPost);
        attr.Should().NotBeNull();
        attr!.AttributeType.FullName.Should().Be(AttributeHelper.Names.HttpGet);
    }

    // --- HasAttributeStartingWith ---

    [Fact]
    public void HasAttributeStartingWith_MatchesPrefix()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUsers = controller.GetMethods().First(m => m.Name == "GetUsers");

        AttributeHelper.HasAttributeStartingWith(getUsers, "Microsoft.AspNetCore.Mvc.Http").Should().BeTrue();
    }

    [Fact]
    public void HasAttributeStartingWith_ReturnsFalseForNoMatch()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUsers = controller.GetMethods().First(m => m.Name == "GetUsers");

        AttributeHelper.HasAttributeStartingWith(getUsers, "Some.Unknown.Prefix").Should().BeFalse();
    }

    // --- GetConstructorArgument ---

    [Fact]
    public void GetConstructorArgument_ReturnsValue()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var routeAttr = AttributeHelper.GetAttribute(controller, AttributeHelper.Names.Route)!;

        AttributeHelper.GetConstructorArgument<string>(routeAttr, 0).Should().Be("api/v1/[controller]");
    }

    [Fact]
    public void GetConstructorArgument_ReturnsDefaultForOutOfRange()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var routeAttr = AttributeHelper.GetAttribute(controller, AttributeHelper.Names.Route)!;

        AttributeHelper.GetConstructorArgument<string>(routeAttr, 99).Should().BeNull();
    }

    // --- GetNamedArgument ---

    [Fact]
    public void GetNamedArgument_ReturnsValue()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUsers = controller.GetMethods().First(m => m.Name == "GetUsers");
        var swaggerOp = AttributeHelper.GetAttribute(getUsers, AttributeHelper.Names.SwaggerOperation)!;

        AttributeHelper.GetNamedArgument<string>(swaggerOp, "Summary").Should().Be("Get all users");
        AttributeHelper.GetNamedArgument<string>(swaggerOp, "OperationId").Should().Be("GetUsers");
    }

    [Fact]
    public void GetNamedArgument_ReturnsDefaultForMissing()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUsers = controller.GetMethods().First(m => m.Name == "GetUsers");
        var swaggerOp = AttributeHelper.GetAttribute(getUsers, AttributeHelper.Names.SwaggerOperation)!;

        AttributeHelper.GetNamedArgument<string>(swaggerOp, "NonexistentProperty").Should().BeNull();
    }

    // --- ParameterInfo overloads ---

    [Fact]
    public void HasAttribute_Parameter_ReturnsTrueForExisting()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUser = controller.GetMethods().First(m => m.Name == "GetUser");
        var idParam = getUser.GetParameters()[0];

        AttributeHelper.HasAttribute(idParam, AttributeHelper.Names.FromRoute).Should().BeTrue();
    }

    [Fact]
    public void HasAttribute_Parameter_ReturnsFalseForMissing()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUser = controller.GetMethods().First(m => m.Name == "GetUser");
        var idParam = getUser.GetParameters()[0];

        AttributeHelper.HasAttribute(idParam, AttributeHelper.Names.FromQuery).Should().BeFalse();
    }

    [Fact]
    public void GetAttribute_Parameter_ReturnsData()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUser = controller.GetMethods().First(m => m.Name == "GetUser");
        var idParam = getUser.GetParameters()[0];

        var attr = AttributeHelper.GetAttribute(idParam, AttributeHelper.Names.SwaggerParameter);
        attr.Should().NotBeNull();
        AttributeHelper.GetConstructorArgument<string>(attr!, 0).Should().Be("User unique identifier");
    }

    [Fact]
    public void GetAttributes_Parameter_ReturnsAll()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var getUser = controller.GetMethods().First(m => m.Name == "GetUser");
        var idParam = getUser.GetParameters()[0];

        var attrs = AttributeHelper.GetAttributes(idParam, AttributeHelper.Names.FromRoute).ToList();
        attrs.Should().HaveCount(1);
    }

    // --- ApiExplorerSettings IgnoreApi ---

    [Fact]
    public void GetNamedArgument_BoolValue()
    {
        var internalCtrl = _loader.Assembly.GetType("SampleApi.Controllers.InternalController")!;
        var attr = AttributeHelper.GetAttribute(internalCtrl, AttributeHelper.Names.ApiExplorerSettings)!;

        AttributeHelper.GetNamedArgument<bool>(attr, "IgnoreApi").Should().BeTrue();
    }

    // --- SwaggerTag on controller ---

    [Fact]
    public void GetAttribute_SwaggerTag_ReadsDescription()
    {
        var controller = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        var attr = AttributeHelper.GetAttribute(controller, AttributeHelper.Names.SwaggerTag);
        attr.Should().NotBeNull();
        AttributeHelper.GetConstructorArgument<string>(attr!, 0)
            .Should().Contain("CRUD operations for users");
    }

    // --- Validation attributes on properties ---

    [Fact]
    public void GetAttribute_StringLength_ReadsMaxAndMin()
    {
        var userDto = _loader.Assembly.GetType("SampleApi.Models.UserDto")!;
        var displayName = userDto.GetProperty("DisplayName")!;

        var attr = AttributeHelper.GetAttribute(displayName, AttributeHelper.Names.StringLength)!;
        // [StringLength(100, MinimumLength = 2)]
        AttributeHelper.GetConstructorArgument<int>(attr, 0).Should().Be(100);
        AttributeHelper.GetNamedArgument<int>(attr, "MinimumLength").Should().Be(2);
    }

    [Fact]
    public void GetAttribute_Range_ReadsMinMax()
    {
        var profile = _loader.Assembly.GetType("SampleApi.Models.UserProfile")!;
        var age = profile.GetProperty("Age")!;

        var attr = AttributeHelper.GetAttribute(age, AttributeHelper.Names.Range)!;
        // [Range(0, 150)]
        AttributeHelper.GetConstructorArgument<int>(attr, 0).Should().Be(0);
        AttributeHelper.GetConstructorArgument<int>(attr, 1).Should().Be(150);
    }
}
