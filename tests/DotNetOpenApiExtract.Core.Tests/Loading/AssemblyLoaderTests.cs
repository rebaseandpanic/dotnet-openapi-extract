using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Loading;

public class AssemblyLoaderTests : IDisposable
{
    private static readonly string SampleApiDll = TestPaths.SampleApiDll;

    private readonly AssemblyLoader _loader;

    public AssemblyLoaderTests()
    {
        _loader = new AssemblyLoader(SampleApiDll);
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    [Fact]
    public void Constructor_LoadsAssembly()
    {
        _loader.Assembly.Should().NotBeNull();
        _loader.Assembly.GetName().Name.Should().Be("SampleApi");
    }

    [Fact]
    public void Constructor_ThrowsOnMissingFile()
    {
        var act = () => new AssemblyLoader("/nonexistent/path.dll");
        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*path.dll*");
    }

    [Fact]
    public void Assembly_ContainsExpectedTypes()
    {
        var types = _loader.Assembly.GetTypes();
        var typeNames = types.Select(t => t.FullName).ToList();

        typeNames.Should().Contain("SampleApi.Controllers.UsersController");
        typeNames.Should().Contain("SampleApi.Controllers.HealthController");
        typeNames.Should().Contain("SampleApi.Controllers.InternalController");
        typeNames.Should().Contain("SampleApi.Models.UserDto");
        typeNames.Should().Contain("SampleApi.Models.CreateUserRequest");
        typeNames.Should().Contain("SampleApi.Models.UserStatus");
    }

    [Fact]
    public void Assembly_CanReadGenericTypes()
    {
        var apiResponseType = _loader.Assembly.GetType("SampleApi.Models.ApiResponse`1");
        apiResponseType.Should().NotBeNull();
        apiResponseType!.IsGenericTypeDefinition.Should().BeTrue();
        apiResponseType.GetGenericArguments().Should().HaveCount(1);
    }

    [Fact]
    public void FindType_FindsTypeInMainAssembly()
    {
        var type = _loader.FindType("SampleApi.Models.UserDto");
        type.Should().NotBeNull();
        type!.Name.Should().Be("UserDto");
    }

    [Fact]
    public void FindType_FindsTypeInReferencedAssembly()
    {
        // ControllerBase lives in Microsoft.AspNetCore.Mvc.Core
        var type = _loader.FindType("Microsoft.AspNetCore.Mvc.ControllerBase");
        type.Should().NotBeNull();
    }

    [Fact]
    public void FindType_ReturnsNullForUnknownType()
    {
        var type = _loader.FindType("Some.Nonexistent.Type");
        type.Should().BeNull();
    }

    [Fact]
    public void Assembly_CanReadCustomAttributes()
    {
        var controllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController");
        controllerType.Should().NotBeNull();

        var attributes = controllerType!.GetCustomAttributesData();
        var attrNames = attributes.Select(a => a.AttributeType.FullName).ToList();

        attrNames.Should().Contain("Microsoft.AspNetCore.Mvc.ApiControllerAttribute");
        attrNames.Should().Contain("Microsoft.AspNetCore.Mvc.RouteAttribute");
    }

    [Fact]
    public void Assembly_CanReadAttributeConstructorArguments()
    {
        var controllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController");
        var routeAttr = controllerType!.GetCustomAttributesData()
            .First(a => a.AttributeType.FullName == "Microsoft.AspNetCore.Mvc.RouteAttribute");

        // [Route("api/v1/[controller]")]
        routeAttr.ConstructorArguments.Should().HaveCount(1);
        routeAttr.ConstructorArguments[0].Value.Should().Be("api/v1/[controller]");
    }

    [Fact]
    public void Assembly_CanReadAttributeNamedArguments()
    {
        var controllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController");
        var methods = controllerType!.GetMethods();
        var getUsers = methods.First(m => m.Name == "GetUsers");

        var swaggerOp = getUsers.GetCustomAttributesData()
            .First(a => a.AttributeType.FullName == "Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute");

        var namedArgs = swaggerOp.NamedArguments.ToDictionary(a => a.MemberName, a => a.TypedValue.Value);
        namedArgs["Summary"].Should().Be("Get all users");
        namedArgs["OperationId"].Should().Be("GetUsers");
    }

    [Fact]
    public void Assembly_CanReadMethodParameters()
    {
        var controllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController");
        var getUser = controllerType!.GetMethods().First(m => m.Name == "GetUser");
        var parameters = getUser.GetParameters();

        parameters.Should().HaveCount(1);
        parameters[0].Name.Should().Be("id");
        parameters[0].ParameterType.FullName.Should().Be("System.Guid");
    }

    [Fact]
    public void Assembly_CanReadParameterAttributes()
    {
        var controllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController");
        var getUser = controllerType!.GetMethods().First(m => m.Name == "GetUser");
        var idParam = getUser.GetParameters()[0];

        var attrNames = idParam.GetCustomAttributesData()
            .Select(a => a.AttributeType.FullName).ToList();

        attrNames.Should().Contain("Microsoft.AspNetCore.Mvc.FromRouteAttribute");
        attrNames.Should().Contain("Swashbuckle.AspNetCore.Annotations.SwaggerParameterAttribute");
    }

    [Fact]
    public void Assembly_CanReadPropertyInfo()
    {
        var userDto = _loader.Assembly.GetType("SampleApi.Models.UserDto");
        var properties = userDto!.GetProperties();
        var propNames = properties.Select(p => p.Name).ToList();

        propNames.Should().Contain("Id");
        propNames.Should().Contain("Email");
        propNames.Should().Contain("DisplayName");
        propNames.Should().Contain("Status");
        propNames.Should().Contain("CreatedAt");
        propNames.Should().Contain("Profile");
        propNames.Should().Contain("Tags");
        propNames.Should().Contain("Metadata");
    }

    [Fact]
    public void Assembly_CanReadNullableTypes()
    {
        var userDto = _loader.Assembly.GetType("SampleApi.Models.UserDto");
        var profileProp = userDto!.GetProperty("Profile")!;

        // Profile is UserProfile? — nullable reference type
        profileProp.PropertyType.FullName.Should().Be("SampleApi.Models.UserProfile");
    }

    [Fact]
    public void Assembly_CanReadCollectionTypes()
    {
        var userDto = _loader.Assembly.GetType("SampleApi.Models.UserDto");

        // List<string> Tags
        var tagsProp = userDto!.GetProperty("Tags")!;
        tagsProp.PropertyType.IsGenericType.Should().BeTrue();
        tagsProp.PropertyType.GetGenericArguments()[0].FullName.Should().Be("System.String");

        // Dictionary<string, string>? Metadata
        var metaProp = userDto.GetProperty("Metadata")!;
        metaProp.PropertyType.IsGenericType.Should().BeTrue();
        var genArgs = metaProp.PropertyType.GetGenericArguments();
        genArgs.Should().HaveCount(2);
        genArgs[0].FullName.Should().Be("System.String");
        genArgs[1].FullName.Should().Be("System.String");
    }

    [Fact]
    public void Assembly_CanReadEnumValues()
    {
        var statusEnum = _loader.Assembly.GetType("SampleApi.Models.UserStatus");
        statusEnum.Should().NotBeNull();
        statusEnum!.IsEnum.Should().BeTrue();

        // Use GetFields instead of Enum.GetNames — safer for MetadataLoadContext types
        var names = statusEnum.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(f => f.Name)
            .ToArray();
        names.Should().BeEquivalentTo("Active", "Suspended", "Banned", "Deleted");
    }

    [Fact]
    public void Assembly_CanReadValidationAttributes()
    {
        var userDto = _loader.Assembly.GetType("SampleApi.Models.UserDto");
        var emailProp = userDto!.GetProperty("Email")!;

        var attrNames = emailProp.GetCustomAttributesData()
            .Select(a => a.AttributeType.FullName).ToList();

        attrNames.Should().Contain("System.ComponentModel.DataAnnotations.RequiredAttribute");
        attrNames.Should().Contain("System.ComponentModel.DataAnnotations.EmailAddressAttribute");
        attrNames.Should().Contain("System.ComponentModel.DataAnnotations.StringLengthAttribute");

        // Check StringLength(255)
        var strLen = emailProp.GetCustomAttributesData()
            .First(a => a.AttributeType.FullName == "System.ComponentModel.DataAnnotations.StringLengthAttribute");
        strLen.ConstructorArguments[0].Value.Should().Be(255);
    }

    [Fact]
    public void Assembly_CanReadInheritanceChain()
    {
        var controllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController");
        var baseType = controllerType!.BaseType;

        baseType.Should().NotBeNull();
        baseType!.FullName.Should().Be("Microsoft.AspNetCore.Mvc.ControllerBase");
    }

    [Fact]
    public void Assembly_CanReadReturnTypes()
    {
        var controllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController");
        var getUser = controllerType!.GetMethods().First(m => m.Name == "GetUser");
        var returnType = getUser.ReturnType;

        // ActionResult<ApiResponse<UserDto>>
        returnType.IsGenericType.Should().BeTrue();
        returnType.GetGenericTypeDefinition().Name.Should().Be("ActionResult`1");

        var innerType = returnType.GetGenericArguments()[0];
        innerType.IsGenericType.Should().BeTrue();
        innerType.GetGenericTypeDefinition().FullName.Should().Be("SampleApi.Models.ApiResponse`1");

        var dataType = innerType.GetGenericArguments()[0];
        dataType.FullName.Should().Be("SampleApi.Models.UserDto");
    }

    [Fact]
    public void Assembly_CanReadApiExplorerSettings()
    {
        var internalCtrl = _loader.Assembly.GetType("SampleApi.Controllers.InternalController");
        var apiExplorer = internalCtrl!.GetCustomAttributesData()
            .First(a => a.AttributeType.FullName == "Microsoft.AspNetCore.Mvc.ApiExplorerSettingsAttribute");

        var ignoreApi = apiExplorer.NamedArguments
            .First(a => a.MemberName == "IgnoreApi");
        ignoreApi.TypedValue.Value.Should().Be(true);
    }

    // =========================================================================
    // GetXmlDocumentationFiles tests
    // =========================================================================

    [Fact]
    public void GetXmlDocumentationFiles_ReturnsOnlyXmlsWithDllSibling()
    {
        var xmlFiles = _loader.GetXmlDocumentationFiles();

        // Every returned XML file must have a sibling DLL
        foreach (var xmlFile in xmlFiles)
        {
            var dllSibling = Path.ChangeExtension(xmlFile, ".dll");
            File.Exists(dllSibling).Should().BeTrue(
                because: $"'{xmlFile}' was returned but has no sibling DLL");
        }
    }

    [Fact]
    public void GetXmlDocumentationFiles_ReturnsNoDuplicates()
    {
        var xmlFiles = _loader.GetXmlDocumentationFiles();

        xmlFiles.Should().OnlyHaveUniqueItems(
            because: "GetXmlDocumentationFiles must not return duplicate paths");
    }

    [Fact]
    public void GetXmlDocumentationFiles_IncludesProjectXml()
    {
        var xmlFiles = _loader.GetXmlDocumentationFiles();
        var projectXml = Path.ChangeExtension(TestPaths.SampleApiDll, ".xml");

        // If the project XML exists (it should — SampleApi has GenerateDocumentationFile=true),
        // it must appear in the result.
        if (File.Exists(projectXml))
        {
            xmlFiles.Should().Contain(projectXml,
                because: "the project XML alongside the target DLL must be included");
        }
    }

    [Fact]
    public void GetXmlDocumentationFiles_ReturnsNonEmptyListOnStandardSdk()
    {
        // On a standard .NET SDK installation (which is always present in CI/dev),
        // there should be at least the runtime XML files from the ref packs.
        var xmlFiles = _loader.GetXmlDocumentationFiles();

        // We know at minimum that the project directory has SampleApi.xml (GenerateDocumentationFile=true)
        xmlFiles.Should().NotBeEmpty(
            because: "there must be at least the project XML in the result");
    }

    [Fact]
    public void MissingRefPackHints_ReturnsListOrEmpty()
    {
        // MissingRefPackHints is non-null but may be empty if ref packs are installed.
        // We just verify the property is accessible and returns a valid (non-null) list.
        _loader.MissingRefPackHints.Should().NotBeNull(
            because: "MissingRefPackHints must always return a non-null list");
    }
}
