using System.Reflection;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Documentation;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Documentation;

/// <summary>
/// Tests for <see cref="XmlDocParser"/> covering construction, type docs, method docs,
/// parameter docs, response docs, property docs, field/enum docs, and edge cases.
/// </summary>
public class XmlDocParserTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Infrastructure
    // -------------------------------------------------------------------------

    private readonly AssemblyLoader _loader;
    private readonly XmlDocParser _parser;

    // Resolved types — resolved once from the loaded assembly
    private readonly Type _usersControllerType;
    private readonly Type _healthControllerType;
    private readonly Type _userDtoType;
    private readonly Type _userStatusType;
    private readonly Type _apiResponseOpenType;  // ApiResponse`1 (generic type definition)

    public XmlDocParserTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        _parser = new XmlDocParser(TestPaths.SampleApiXml);

        _usersControllerType = _loader.Assembly.GetType("SampleApi.Controllers.UsersController")!;
        _healthControllerType = _loader.Assembly.GetType("SampleApi.Controllers.HealthController")!;
        _userDtoType = _loader.Assembly.GetType("SampleApi.Models.UserDto")!;
        _userStatusType = _loader.Assembly.GetType("SampleApi.Models.UserStatus")!;
        _apiResponseOpenType = _loader.Assembly.GetType("SampleApi.Models.ApiResponse`1")!;
    }

    public void Dispose()
    {
        _loader.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helper to retrieve a method by name from a type
    // -------------------------------------------------------------------------

    private static MethodInfo GetMethod(Type type, string name)
        => type.GetMethods().Single(m => m.Name == name);

    // =========================================================================
    // Constructor tests
    // =========================================================================

    [Fact]
    public void Constructor_NullPath_CreatesEmptyParser()
    {
        // Should not throw, and lookups return null
        var parser = new XmlDocParser(null);
        parser.GetTypeDoc(typeof(string)).Should().BeNull();
    }

    [Fact]
    public void Constructor_NonExistentPath_CreatesEmptyParser()
    {
        var parser = new XmlDocParser("/nonexistent/path/to/missing.xml");
        parser.GetTypeDoc(typeof(object)).Should().BeNull();
    }

    [Fact]
    public void Constructor_ValidPath_LoadsWithoutException()
    {
        // The _parser field is constructed in the test constructor — if it threw,
        // the test would never reach here.
        _parser.Should().NotBeNull();
    }

    // =========================================================================
    // Type docs
    // =========================================================================

    [Fact]
    public void GetTypeDoc_UserDto_HasExpectedSummary()
    {
        var entry = _parser.GetTypeDoc(_userDtoType);

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("User data transfer object");
    }

    [Fact]
    public void GetTypeDoc_UserStatus_HasExpectedSummary()
    {
        var entry = _parser.GetTypeDoc(_userStatusType);

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("User account status");
    }

    [Fact]
    public void GetTypeDoc_ApiResponseGeneric_SummaryContainsWrapper()
    {
        // The open generic type definition has FullName "SampleApi.Models.ApiResponse`1"
        // which maps to XML key "T:SampleApi.Models.ApiResponse`1"
        var entry = _parser.GetTypeDoc(_apiResponseOpenType);

        entry.Should().NotBeNull();
        entry!.Summary.Should().Contain("Standard API response wrapper");
    }

    [Fact]
    public void GetTypeDoc_UnknownType_ReturnsNull()
    {
        // System.String is not in SampleApi.xml
        var entry = _parser.GetTypeDoc(typeof(string));
        entry.Should().BeNull();
    }

    // =========================================================================
    // Method docs — UsersController actions
    // =========================================================================

    [Fact]
    public void GetMethodDoc_GetUsers_HasExpectedSummary()
    {
        var method = GetMethod(_usersControllerType, "GetUsers");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("Get all users");
    }

    [Fact]
    public void GetMethodDoc_GetUsers_RemarksContainsPaginatedList()
    {
        var method = GetMethod(_usersControllerType, "GetUsers");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Remarks.Should().Contain("paginated list");
    }

    [Fact]
    public void GetMethodDoc_GetUser_HasExpectedSummary()
    {
        var method = GetMethod(_usersControllerType, "GetUser");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("Get user by ID");
    }

    [Fact]
    public void GetMethodDoc_CreateUser_HasExpectedSummary()
    {
        var method = GetMethod(_usersControllerType, "CreateUser");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("Create a new user");
    }

    // =========================================================================
    // Parameter docs
    // =========================================================================

    [Fact]
    public void GetMethodDoc_GetUsers_ParameterStatus_HasExpectedDescription()
    {
        var method = GetMethod(_usersControllerType, "GetUsers");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Parameters.Should().ContainKey("status");
        entry.Parameters["status"].Should().Be("Filter by user status");
    }

    [Fact]
    public void GetMethodDoc_GetUsers_ParameterPage_HasExpectedDescription()
    {
        var method = GetMethod(_usersControllerType, "GetUsers");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Parameters.Should().ContainKey("page");
        entry.Parameters["page"].Should().Be("Page number (1-based)");
    }

    [Fact]
    public void GetMethodDoc_GetUser_ParameterId_DescriptionContainsUniqueIdentifier()
    {
        var method = GetMethod(_usersControllerType, "GetUser");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Parameters.Should().ContainKey("id");
        entry.Parameters["id"].Should().Contain("unique identifier");
    }

    [Fact]
    public void GetMethodDoc_CreateUser_ParameterRequest_HasExpectedDescription()
    {
        var method = GetMethod(_usersControllerType, "CreateUser");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Parameters.Should().ContainKey("request");
        entry.Parameters["request"].Should().Be("User creation data");
    }

    // =========================================================================
    // Response docs
    // =========================================================================

    [Fact]
    public void GetMethodDoc_GetUser_Response200_HasExpectedDescription()
    {
        var method = GetMethod(_usersControllerType, "GetUser");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Responses.Should().ContainKey("200");
        entry.Responses["200"].Should().Be("User found");
    }

    [Fact]
    public void GetMethodDoc_GetUser_Response422_HasExpectedDescription()
    {
        var method = GetMethod(_usersControllerType, "GetUser");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Responses.Should().ContainKey("422");
        entry.Responses["422"].Should().Be("User not found");
    }

    [Fact]
    public void GetMethodDoc_CreateUser_HasResponses201_400_422()
    {
        var method = GetMethod(_usersControllerType, "CreateUser");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Responses.Keys.Should().Contain("201");
        entry.Responses.Keys.Should().Contain("400");
        entry.Responses.Keys.Should().Contain("422");
    }

    // =========================================================================
    // Property docs
    // =========================================================================

    [Fact]
    public void GetPropertyDoc_UserDto_Email_HasExpectedSummary()
    {
        var entry = _parser.GetPropertyDoc(_userDtoType, "Email");

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("User email address");
    }

    [Fact]
    public void GetPropertyDoc_UserDto_Id_HasExpectedSummary()
    {
        var entry = _parser.GetPropertyDoc(_userDtoType, "Id");

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("User unique identifier");
    }

    [Fact]
    public void GetPropertyDoc_UserDto_Status_HasExpectedSummary()
    {
        var entry = _parser.GetPropertyDoc(_userDtoType, "Status");

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("User account status");
    }

    [Fact]
    public void GetPropertyDoc_ApiResponse_Success_SummaryContainsWhetherTheOperation()
    {
        // ApiResponse<T>.Success — the open generic type definition is used as the declaring type
        var entry = _parser.GetPropertyDoc(_apiResponseOpenType, "Success");

        entry.Should().NotBeNull();
        entry!.Summary.Should().Contain("Whether the operation was successful");
    }

    // =========================================================================
    // Field / enum docs
    // =========================================================================

    [Fact]
    public void GetFieldDoc_UserStatus_Active_HasExpectedSummary()
    {
        var entry = _parser.GetFieldDoc(_userStatusType, "Active");

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("Active account");
    }

    [Fact]
    public void GetFieldDoc_UserStatus_Banned_HasExpectedSummary()
    {
        var entry = _parser.GetFieldDoc(_userStatusType, "Banned");

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("Banned account");
    }

    [Fact]
    public void GetFieldDoc_ConnectionState_NoSummary_ReturnsNull()
    {
        // ConnectionState values have no XML <summary> — GetFieldDoc should return null
        var connectionStateType = _loader.Assembly.GetType("SampleApi.Models.ConnectionState")!;
        var entry = _parser.GetFieldDoc(connectionStateType, "Connected");

        entry.Should().BeNull();
    }

    [Fact]
    public void GetFieldDoc_TrafficLight_DocumentedValue_HasSummary()
    {
        var trafficLightType = _loader.Assembly.GetType("SampleApi.Models.TrafficLight")!;
        var entry = _parser.GetFieldDoc(trafficLightType, "Red");

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("Stop — vehicles must halt.");
    }

    [Fact]
    public void GetFieldDoc_TrafficLight_UndocumentedValue_ReturnsNull()
    {
        // Yellow has no XML <summary>
        var trafficLightType = _loader.Assembly.GetType("SampleApi.Models.TrafficLight")!;
        var entry = _parser.GetFieldDoc(trafficLightType, "Yellow");

        entry.Should().BeNull();
    }

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Fact]
    public void GetMethodDoc_GetUsers_GenericReturnType_KeyMatchesXml()
    {
        // GetUsers returns ApiResponse<List<UserDto>> — the method key is built from
        // parameter types, not the return type. This verifies the method is found at all,
        // demonstrating that the Nullable<UserStatus> parameter is keyed correctly as
        // "System.Nullable{SampleApi.Models.UserStatus}" to match the XML.
        var method = GetMethod(_usersControllerType, "GetUsers");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull(
            because: "GetUsers must be resolvable despite its Nullable<UserStatus> parameter");
        entry!.Summary.Should().Be("Get all users");
    }

    [Fact]
    public void GetMethodDoc_HealthControllerHealthz_NoParameters_WorksCorrectly()
    {
        // Healthz has no parameters — BuildMethodKey omits the parameter list entirely
        var method = GetMethod(_healthControllerType, "Healthz");
        var entry = _parser.GetMethodDoc(method);

        entry.Should().NotBeNull();
        entry!.Summary.Should().Be("Liveness probe");
    }
}
