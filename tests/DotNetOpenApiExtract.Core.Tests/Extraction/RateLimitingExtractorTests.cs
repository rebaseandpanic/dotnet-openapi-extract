using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="RateLimitingExtractor"/>.
/// Tests load SampleApi.dll and exercise attribute extraction from the
/// <c>RateLimitingController</c> fixture.
/// </summary>
public class RateLimitingExtractorTests : IDisposable
{
    private readonly AssemblyLoader _loader;
    private readonly ControllerInfo _rateLimitingController;
    private readonly ActionInfo _getAction;           // GET api/rl     — inherits controller policy
    private readonly ActionInfo _getOverrideAction;   // GET api/rl/override — action-level policy
    private readonly ActionInfo _getDisabledAction;   // GET api/rl/disabled — [DisableRateLimiting]
    private readonly ActionInfo _getDisableWinsAction; // GET api/rl/disable-wins — both attrs, Disable wins
    private readonly ControllerInfo _usersController;
    private readonly ActionInfo _getUsersAction;      // GET api/v1/users — no rate-limiting attributes

    public RateLimitingExtractorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        var controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);

        _rateLimitingController = controllers.Single(c => c.Name == "RateLimiting");
        _usersController        = controllers.Single(c => c.Name == "Users");

        var rlActions = ActionDiscovery.DiscoverActions(_rateLimitingController);
        _getAction           = rlActions.Single(a => a.Name == "Get");
        _getOverrideAction   = rlActions.Single(a => a.Name == "GetOverride");
        _getDisabledAction   = rlActions.Single(a => a.Name == "GetDisabled");
        _getDisableWinsAction = rlActions.Single(a => a.Name == "GetDisableWins");

        var usersActions    = ActionDiscovery.DiscoverActions(_usersController);
        _getUsersAction     = usersActions.Single(a => a.Name == "GetUsers");
    }

    public void Dispose() => _loader.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // 1. [EnableRateLimiting] on action returns action policy
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_EnableRateLimitingOnAction_ReturnsPolicy()
    {
        var info = RateLimitingExtractor.Extract(_rateLimitingController, _getOverrideAction);

        info.Should().NotBeNull();
        info!.IsDisabled.Should().BeFalse();
        info.PolicyName.Should().Be("strict");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. [EnableRateLimiting] on controller — all actions inherit
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_EnableRateLimitingOnController_AllActionsInherit()
    {
        var info = RateLimitingExtractor.Extract(_rateLimitingController, _getAction);

        info.Should().NotBeNull();
        info!.IsDisabled.Should().BeFalse();
        info.PolicyName.Should().Be("default-policy");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Action-level [EnableRateLimiting] overrides controller-level
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ActionOverridesController()
    {
        // GetOverride has [EnableRateLimiting("strict")] while controller has "default-policy"
        var info = RateLimitingExtractor.Extract(_rateLimitingController, _getOverrideAction);

        info.Should().NotBeNull();
        info!.PolicyName.Should().Be("strict",
            because: "action-level [EnableRateLimiting] takes precedence over controller-level");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. [DisableRateLimiting] on action → IsDisabled = true
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_DisableOnAction_IsDisabledTrue()
    {
        var info = RateLimitingExtractor.Extract(_rateLimitingController, _getDisabledAction);

        info.Should().NotBeNull();
        info!.IsDisabled.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. No attributes → returns null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NoAttributes_ReturnsNull()
    {
        // UsersController has no rate-limiting attributes
        var info = RateLimitingExtractor.Extract(_usersController, _getUsersAction);

        info.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. [DisableRateLimiting] + [EnableRateLimiting] on same action → Disable wins
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_DisableAndEnableOnSameAction_DisableWins()
    {
        var info = RateLimitingExtractor.Extract(_rateLimitingController, _getDisableWinsAction);

        info.Should().NotBeNull();
        info!.IsDisabled.Should().BeTrue(
            because: "[DisableRateLimiting] must win over [EnableRateLimiting] when both are present on the action");
        info.PolicyName.Should().Be(string.Empty,
            because: "no policy is active when rate limiting is disabled");
    }
}
