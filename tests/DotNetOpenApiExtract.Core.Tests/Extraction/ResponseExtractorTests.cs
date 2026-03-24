using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Comprehensive unit tests for <see cref="ResponseExtractor"/>.
///
/// Setup pattern: load SampleApi.dll via <see cref="AssemblyLoader"/>,
/// discover controllers and actions, then call <see cref="ResponseExtractor.ExtractResponses"/>
/// on the action under test.
/// </summary>
public class ResponseExtractorTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Infrastructure
    // -------------------------------------------------------------------------

    private readonly AssemblyLoader _loader;
    private readonly IReadOnlyList<ControllerInfo> _controllers;

    // Actions resolved once per test class instance.
    private readonly IReadOnlyList<ActionInfo> _usersActions;
    private readonly IReadOnlyList<ActionInfo> _filesActions;
    private readonly IReadOnlyList<ActionInfo> _healthActions;

    public ResponseExtractorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        _controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);

        _usersActions  = ActionDiscovery.DiscoverActions(_controllers.Single(c => c.Name == "Users"));
        _filesActions  = ActionDiscovery.DiscoverActions(_controllers.Single(c => c.Name == "Files"));
        _healthActions = ActionDiscovery.DiscoverActions(_controllers.Single(c => c.Name == "Health"));
    }

    public void Dispose() => _loader.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<ResponseInfo> Extract(IReadOnlyList<ActionInfo> actions, string actionName)
        => ResponseExtractor.ExtractResponses(actions.Single(a => a.Name == actionName));

    private static ResponseInfo? ForStatus(IReadOnlyList<ResponseInfo> responses, int statusCode)
        => responses.FirstOrDefault(r => r.StatusCode == statusCode);

    // =========================================================================
    // UsersController.GetUsers — single response with generic body type
    // =========================================================================

    [Fact]
    public void GetUsers_HasExactlyOneResponse()
    {
        var responses = Extract(_usersActions, "GetUsers");
        responses.Should().HaveCount(1);
    }

    [Fact]
    public void GetUsers_Status200HasBodyType()
    {
        var responses = Extract(_usersActions, "GetUsers");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
        r200!.BodyType.Should().NotBeNull();
    }

    [Fact]
    public void GetUsers_Status200BodyTypeContainsApiResponse()
    {
        var responses = Extract(_usersActions, "GetUsers");
        var r200 = ForStatus(responses, 200);

        // ProducesResponseType(typeof(ApiResponse<List<UserDto>>), 200)
        r200!.BodyType!.Name.Should().Contain("ApiResponse");
    }

    // =========================================================================
    // UsersController.GetUser — two responses, 422 has no body
    // =========================================================================

    [Fact]
    public void GetUser_HasExactlyTwoResponses()
    {
        var responses = Extract(_usersActions, "GetUser");
        responses.Should().HaveCount(2);
    }

    [Fact]
    public void GetUser_Status200HasBodyType()
    {
        var responses = Extract(_usersActions, "GetUser");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
        r200!.BodyType.Should().NotBeNull();
    }

    [Fact]
    public void GetUser_Status422HasNoBodyType()
    {
        var responses = Extract(_usersActions, "GetUser");
        var r422 = ForStatus(responses, 422);

        r422.Should().NotBeNull();
        r422!.BodyType.Should().BeNull();
    }

    // =========================================================================
    // UsersController.CreateUser — three responses (201, 400, 422)
    // =========================================================================

    [Fact]
    public void CreateUser_HasExactlyThreeResponses()
    {
        var responses = Extract(_usersActions, "CreateUser");
        responses.Should().HaveCount(3);
    }

    [Fact]
    public void CreateUser_ContainsStatusCodes201_400_422()
    {
        var responses = Extract(_usersActions, "CreateUser");
        var codes = responses.Select(r => r.StatusCode).ToList();

        codes.Should().Contain(201);
        codes.Should().Contain(400);
        codes.Should().Contain(422);
    }

    [Fact]
    public void CreateUser_Status201HasBodyType()
    {
        var responses = Extract(_usersActions, "CreateUser");
        var r201 = ForStatus(responses, 201);

        r201.Should().NotBeNull();
        r201!.BodyType.Should().NotBeNull();
    }

    [Fact]
    public void CreateUser_Status400HasNoBodyType()
    {
        var responses = Extract(_usersActions, "CreateUser");
        var r400 = ForStatus(responses, 400);

        r400.Should().NotBeNull();
        r400!.BodyType.Should().BeNull();
    }

    [Fact]
    public void CreateUser_Status422HasNoBodyType()
    {
        var responses = Extract(_usersActions, "CreateUser");
        var r422 = ForStatus(responses, 422);

        r422.Should().NotBeNull();
        r422!.BodyType.Should().BeNull();
    }

    // =========================================================================
    // UsersController.DeleteUser — 204 (no body) + 422
    // =========================================================================

    [Fact]
    public void DeleteUser_HasExactlyTwoResponses()
    {
        var responses = Extract(_usersActions, "DeleteUser");
        responses.Should().HaveCount(2);
    }

    [Fact]
    public void DeleteUser_ContainsStatusCodes204And422()
    {
        var responses = Extract(_usersActions, "DeleteUser");
        var codes = responses.Select(r => r.StatusCode).ToList();

        codes.Should().Contain(204);
        codes.Should().Contain(422);
    }

    [Fact]
    public void DeleteUser_Status204HasNoBodyType()
    {
        var responses = Extract(_usersActions, "DeleteUser");
        var r204 = ForStatus(responses, 204);

        r204.Should().NotBeNull();
        r204!.BodyType.Should().BeNull();
    }

    // =========================================================================
    // HealthController.Healthz — simple 200 with no body
    // =========================================================================

    [Fact]
    public void Healthz_HasExactlyOneResponse()
    {
        var responses = Extract(_healthActions, "Healthz");
        responses.Should().HaveCount(1);
    }

    [Fact]
    public void Healthz_Status200HasNoBodyType()
    {
        var responses = Extract(_healthActions, "Healthz");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
        r200!.BodyType.Should().BeNull();
    }

    // =========================================================================
    // FilesController.Upload — 201 with FileUploadResult, 400, 422
    // =========================================================================

    [Fact]
    public void Upload_HasExactlyThreeResponses()
    {
        var responses = Extract(_filesActions, "Upload");
        responses.Should().HaveCount(3);
    }

    [Fact]
    public void Upload_ContainsStatusCodes201_400_422()
    {
        var responses = Extract(_filesActions, "Upload");
        var codes = responses.Select(r => r.StatusCode).ToList();

        codes.Should().Contain(201);
        codes.Should().Contain(400);
        codes.Should().Contain(422);
    }

    [Fact]
    public void Upload_Status201BodyTypeContainsFileUploadResult()
    {
        var responses = Extract(_filesActions, "Upload");
        var r201 = ForStatus(responses, 201);

        r201.Should().NotBeNull();
        r201!.BodyType.Should().NotBeNull();
        r201.BodyType!.Name.Should().Contain("FileUploadResult");
    }

    // =========================================================================
    // FilesController.GetStats — no ProducesResponseType, infer from ActionResult<FileStats>
    // =========================================================================

    [Fact]
    public void GetStats_HasAtLeastOneResponse()
    {
        var responses = Extract(_filesActions, "GetStats");
        responses.Should().NotBeEmpty();
    }

    [Fact]
    public void GetStats_Status200IsPresent()
    {
        var responses = Extract(_filesActions, "GetStats");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
    }

    [Fact]
    public void GetStats_Status200BodyTypeContainsFileStats()
    {
        var responses = Extract(_filesActions, "GetStats");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
        r200!.BodyType.Should().NotBeNull();
        r200.BodyType!.Name.Should().Contain("FileStats");
    }

    // =========================================================================
    // FilesController.DeleteFile — void return with explicit [ProducesResponseType(204)]
    // =========================================================================

    [Fact]
    public void DeleteFile_HasExactlyOneResponse()
    {
        var responses = Extract(_filesActions, "DeleteFile");
        responses.Should().HaveCount(1);
    }

    [Fact]
    public void DeleteFile_Status204IsPresent()
    {
        var responses = Extract(_filesActions, "DeleteFile");
        var r204 = ForStatus(responses, 204);

        r204.Should().NotBeNull();
    }

    [Fact]
    public void DeleteFile_Status204HasNoBodyType()
    {
        var responses = Extract(_filesActions, "DeleteFile");
        var r204 = ForStatus(responses, 204);

        r204.Should().NotBeNull();
        r204!.BodyType.Should().BeNull();
    }

    // =========================================================================
    // Return type unwrapping — UnwrapReturnType (internal, tested via GetStats inference)
    // =========================================================================

    /// <summary>
    /// Verifies that the inferred response for GetStats (ActionResult&lt;FileStats&gt;) correctly
    /// unwraps the generic wrapper and exposes FileStats as the body type, confirming that
    /// Task&lt;ActionResult&lt;T&gt;&gt; and ActionResult&lt;T&gt; chains are unwrapped to T.
    /// </summary>
    [Fact]
    public void UnwrapReturnType_ActionResultOfT_UnwrapsToInnerType()
    {
        // GetStats returns ActionResult<FileStats> with no explicit ProducesResponseType.
        // The extractor must infer a 200 with BodyType == FileStats.
        var responses = Extract(_filesActions, "GetStats");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
        r200!.BodyType.Should().NotBeNull();

        // The unwrapped type should be FileStats, not ActionResult<FileStats>.
        r200.BodyType!.Name.Should().NotContain("ActionResult");
        r200.BodyType.Name.Should().Contain("FileStats");
    }

    /// <summary>
    /// Upload returns Task&lt;ActionResult&lt;FileUploadResult&gt;&gt;.
    /// With explicit ProducesResponseType the extractor reports FileUploadResult for 201.
    /// This verifies that the body type exposed through the attribute is the concrete type.
    /// </summary>
    [Fact]
    public void UnwrapReturnType_TaskOfActionResultOfT_BodyTypeIsConcreteType()
    {
        var responses = Extract(_filesActions, "Upload");
        var r201 = ForStatus(responses, 201);

        r201.Should().NotBeNull();
        r201!.BodyType.Should().NotBeNull();
        r201.BodyType!.Name.Should().Contain("FileUploadResult");
        r201.BodyType.Name.Should().NotContain("Task");
        r201.BodyType.Name.Should().NotContain("ActionResult");
    }

    // =========================================================================
    // Response descriptions and content types
    // =========================================================================

    /// <summary>
    /// All responses should have an integer status code greater than zero (except the
    /// special DefaultStatusCode sentinel which is -1).
    /// </summary>
    [Fact]
    public void AllExtractedResponses_HaveValidStatusCodes()
    {
        var allActions = ActionDiscovery.DiscoverActions(_controllers);

        foreach (var action in allActions)
        {
            var responses = ResponseExtractor.ExtractResponses(action);

            foreach (var response in responses)
            {
                // Status codes must either be -1 (default sentinel) or a valid HTTP status code.
                bool isValid = response.StatusCode == ResponseExtractor.DefaultStatusCode
                            || (response.StatusCode >= 100 && response.StatusCode <= 599);

                isValid.Should().BeTrue(
                    because: $"action '{action.Name}' has response with unexpected status code {response.StatusCode}");
            }
        }
    }

    /// <summary>
    /// Verifies that responses include content types (defaulting to application/json).
    /// </summary>
    [Fact]
    public void GetUsers_Status200ContentTypeIsApplicationJson()
    {
        var responses = Extract(_usersActions, "GetUsers");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
        r200!.ContentTypes.Should().Contain("application/json");
    }

    /// <summary>
    /// Verifies status codes are the correct integer values, not zero or garbage.
    /// </summary>
    [Fact]
    public void GetUser_StatusCodesAreCorrectIntegers()
    {
        var responses = Extract(_usersActions, "GetUser");
        var codes = responses.Select(r => r.StatusCode).ToList();

        codes.Should().Contain(200);
        codes.Should().Contain(422);
        codes.Should().NotContain(0);
    }

    // =========================================================================
    // FilesController.GetStats — no [SwaggerResponse] with description, but confirm Description
    // behaviour for [ProducesResponseType] (no description expected).
    // =========================================================================

    /// <summary>
    /// ProducesResponseType attributes do not carry descriptions by default,
    /// so description should be null for standard attributes.
    /// </summary>
    [Fact]
    public void GetUsers_Status200Description_IsNullOrEmpty()
    {
        var responses = Extract(_usersActions, "GetUsers");
        var r200 = ForStatus(responses, 200);

        r200.Should().NotBeNull();
        // [ProducesResponseType] does not set a description
        string? desc = r200!.Description;
        (desc == null || desc == string.Empty).Should().BeTrue(
            because: "ProducesResponseType does not carry a description");
    }

    // =========================================================================
    // Edge case: no responses should be empty (fallback inference always yields at least 1)
    // =========================================================================

    [Fact]
    public void ExtractResponses_NeverReturnsEmptyList()
    {
        var allActions = ActionDiscovery.DiscoverActions(_controllers);

        foreach (var action in allActions)
        {
            var responses = ResponseExtractor.ExtractResponses(action);
            responses.Should().NotBeEmpty(
                because: $"action '{action.Name}' must always produce at least one response");
        }
    }
}
