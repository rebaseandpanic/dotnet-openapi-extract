using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="AuthorizationExtractor"/>.
/// Tests load SampleApi.dll and exercise attribute extraction from the
/// <c>SecureController</c> fixture.
/// </summary>
public class AuthorizationExtractorTests : IDisposable
{
    private readonly AssemblyLoader _loader;
    private readonly ControllerInfo _secureController;
    private readonly ActionInfo _getAction;       // GET /api/secure — [Authorize] inherited
    private readonly ActionInfo _getPublicAction; // GET /api/secure/public — [AllowAnonymous]
    private readonly ActionInfo _getAdminAction;  // GET /api/secure/admin — [Authorize(Policy,Schemes)]
    private readonly ControllerInfo _usersController;
    private readonly ActionInfo _getUsersAction;  // GET /api/v1/users — no auth attributes

    public AuthorizationExtractorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        var controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);

        _secureController = controllers.Single(c => c.Name == "Secure");
        _usersController  = controllers.Single(c => c.Name == "Users");

        var secureActions = ActionDiscovery.DiscoverActions(_secureController);
        _getAction        = secureActions.Single(a => a.Name == "Get");
        _getPublicAction  = secureActions.Single(a => a.Name == "GetPublic");
        _getAdminAction   = secureActions.Single(a => a.Name == "GetAdmin");

        var usersActions  = ActionDiscovery.DiscoverActions(_usersController);
        _getUsersAction   = usersActions.Single(a => a.Name == "GetUsers");
    }

    public void Dispose() => _loader.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // 7. AllowAnonymous on action → IsAnonymous = true
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AllowAnonymousOnAction_IsAnonymousTrue()
    {
        var info = AuthorizationExtractor.Extract(_secureController, _getPublicAction);

        info.IsAnonymous.Should().BeTrue();
        info.RequiresAuthorization.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Authorize on controller — all actions inherit it
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AuthorizeOnController_AllActionsRequireAuth()
    {
        var info = AuthorizationExtractor.Extract(_secureController, _getAction);

        info.IsAnonymous.Should().BeFalse();
        info.RequiresAuthorization.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. AllowAnonymous on action overrides Authorize on controller
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AllowAnonymousOnActionOverridesAuthorizeOnController()
    {
        var info = AuthorizationExtractor.Extract(_secureController, _getPublicAction);

        info.IsAnonymous.Should().BeTrue(
            because: "[AllowAnonymous] on action overrides [Authorize] on controller");
        info.RequiresAuthorization.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. Authorize with AuthenticationSchemes → schemes parsed and split
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AuthorizeWithSchemes_ReturnsParsedSchemes()
    {
        var info = AuthorizationExtractor.Extract(_secureController, _getAdminAction);

        info.RequiresAuthorization.Should().BeTrue();
        info.AuthenticationSchemes.Should().NotBeNull();
        info.AuthenticationSchemes!.Should().ContainSingle().Which.Should().Be("Bearer");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. Authorize with Policy → policy returned
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AuthorizeWithPolicy_ReturnsPolicy()
    {
        var info = AuthorizationExtractor.Extract(_secureController, _getAdminAction);

        info.RequiresAuthorization.Should().BeTrue();
        info.Policies.Should().NotBeNull();
        info.Policies!.Should().ContainSingle().Which.Should().Be("Admin");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. No auth attributes → not anonymous, not authorized
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NoAttributes_IsAnonymousFalse_RequiresFalse()
    {
        // UsersController has no [Authorize] or [AllowAnonymous]
        var info = AuthorizationExtractor.Extract(_usersController, _getUsersAction);

        info.IsAnonymous.Should().BeFalse();
        info.RequiresAuthorization.Should().BeFalse();
        info.AuthenticationSchemes.Should().BeNull();
        info.Policies.Should().BeNull();
    }
}
