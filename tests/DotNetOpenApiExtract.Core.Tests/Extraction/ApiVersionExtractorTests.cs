using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="ApiVersionExtractor"/>.
/// Loads SampleApi.dll via <see cref="AssemblyLoader"/> and inspects the versioning
/// controllers defined in <c>tests/TestAssemblies/SampleApi/Controllers/VersionedController.cs</c>.
/// </summary>
public sealed class ApiVersionExtractorTests : IDisposable
{
    private readonly AssemblyLoader _loader;
    private readonly IReadOnlyList<ControllerInfo> _controllers;

    public ApiVersionExtractorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        _controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);
    }

    public void Dispose() => _loader.Dispose();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ControllerInfo Controller(string name) =>
        _controllers.Single(c => c.Name == name);

    private static ActionInfo Action(ControllerInfo controller, string httpMethod) =>
        ActionDiscovery.DiscoverActions(controller)
            .Single(a => a.HttpMethod == httpMethod);

    private static ActionInfo ActionByName(ControllerInfo controller, string name) =>
        ActionDiscovery.DiscoverActions(controller)
            .Single(a => a.Name == name);

    // =========================================================================
    // GetSupportedVersions
    // =========================================================================

    /// <summary>
    /// Controller declares [ApiVersion("1.0")] and [ApiVersion("2.0")];
    /// GetAll action has no additional version attributes.
    /// Expected: union is ["1.0", "2.0"].
    /// </summary>
    [Fact]
    public void GetSupportedVersions_ControllerLevelOnly_ReturnsUnion()
    {
        var controller = Controller("Versioned");
        var action = ActionByName(controller, "GetAll");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "1.0", "2.0" });
    }

    /// <summary>
    /// Controller has [ApiVersion("1.0"), ApiVersion("2.0")];
    /// GetV2Only action has [MapToApiVersion("2.0")].
    /// Expected: MapToApiVersion wins → only ["2.0"].
    /// </summary>
    [Fact]
    public void GetSupportedVersions_MapToApiVersionOnAction_OverridesController()
    {
        var controller = Controller("Versioned");
        var action = ActionByName(controller, "GetV2Only");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "2.0" });
    }

    /// <summary>
    /// VersioningUnionController: controller has [ApiVersion("1.0")],
    /// action has [ApiVersion("2.0")].
    /// Expected: union ["1.0", "2.0"].
    /// </summary>
    [Fact]
    public void GetSupportedVersions_ActionAddsVersion_UnionWithController()
    {
        var controller = Controller("VersioningUnion");
        var action = Action(controller, "GET");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "1.0", "2.0" });
    }

    /// <summary>
    /// VersioningDedupController: both controller and action declare [ApiVersion("1.0")].
    /// Expected: deduplication → only ["1.0"] once.
    /// </summary>
    [Fact]
    public void GetSupportedVersions_Dedup()
    {
        var controller = Controller("VersioningDedup");
        var action = Action(controller, "GET");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "1.0" });
        versions.Should().HaveCount(1);
    }

    /// <summary>
    /// UsersController has no versioning attributes at all.
    /// Expected: empty list.
    /// </summary>
    [Fact]
    public void GetSupportedVersions_NoAttributes_ReturnsEmpty()
    {
        var controller = Controller("Users");
        var action = ActionDiscovery.DiscoverActions(controller).First();

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEmpty();
    }

    /// <summary>
    /// VersioningIntConstructorController uses [ApiVersion(1, 0)] (int major, int minor).
    /// Expected: "1.0" extracted via int constructor parsing.
    /// </summary>
    [Fact]
    public void GetSupportedVersions_IntConstructor_ConvertsToString()
    {
        var controller = Controller("VersioningIntConstructor");
        var action = Action(controller, "GET");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "1.0" });
    }

    /// <summary>
    /// VersioningStatusSuffixController uses [ApiVersion(1, 0, "beta")] and [ApiVersion(2, 0, "rc1")].
    /// Expected: ["1.0-beta", "2.0-rc1"] extracted via (int, int, string) constructor parsing.
    /// </summary>
    [Fact]
    public void GetSupportedVersions_IntIntStringConstructor_ReturnsMajorMinorStatus()
    {
        var controller = Controller("VersioningStatusSuffix");
        var action = Action(controller, "GET");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "1.0-beta", "2.0-rc1" });
    }

    /// <summary>
    /// VersioningDoubleStatusSuffixController uses [ApiVersion(1.0, "alpha")].
    /// Expected: ["1.0-alpha"] extracted via (double, string) constructor parsing.
    /// </summary>
    [Fact]
    public void GetSupportedVersions_DoubleStringConstructor_ReturnsFormatted()
    {
        var controller = Controller("VersioningDoubleStatusSuffix");
        var action = Action(controller, "GET");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "1.0-alpha" });
    }

    // =========================================================================
    // IsVersionNeutral
    // =========================================================================

    /// <summary>
    /// StatusController has [ApiVersionNeutral] at the controller level.
    /// Expected: IsVersionNeutral → true.
    /// </summary>
    [Fact]
    public void IsVersionNeutral_ControllerNeutral_True()
    {
        var controller = Controller("Status");
        var action = Action(controller, "GET");

        var result = ApiVersionExtractor.IsVersionNeutral(controller, action);

        result.Should().BeTrue();
    }

    /// <summary>
    /// VersioningActionNeutralController: the controller has [ApiVersion("1.0")]
    /// but the action has [ApiVersionNeutral].
    /// Expected: IsVersionNeutral → true (action-level neutral is sufficient).
    /// </summary>
    [Fact]
    public void IsVersionNeutral_ActionNeutral_True()
    {
        var controller = Controller("VersioningActionNeutral");
        var action = Action(controller, "GET");

        var result = ApiVersionExtractor.IsVersionNeutral(controller, action);

        result.Should().BeTrue();
    }

    /// <summary>
    /// VersionedController has no [ApiVersionNeutral] at any level.
    /// Expected: IsVersionNeutral → false.
    /// </summary>
    [Fact]
    public void IsVersionNeutral_NoNeutralAttribute_False()
    {
        var controller = Controller("Versioned");
        var action = ActionByName(controller, "GetAll");

        var result = ApiVersionExtractor.IsVersionNeutral(controller, action);

        result.Should().BeFalse();
    }

    // =========================================================================
    // Audit gap — bare double constructor
    // =========================================================================

    /// <summary>
    /// VersioningBareDoubleController uses [ApiVersion(1.5)] — the single-argument
    /// double constructor without a status suffix.
    /// Expected: ["1.5"] — distinct branch from (double, string) and (int, int).
    /// </summary>
    [Fact]
    public void GetSupportedVersions_BareDoubleConstructor_ConvertsToString()
    {
        var controller = Controller("VersioningBareDouble");
        var action = Action(controller, "GET");

        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        versions.Should().BeEquivalentTo(new[] { "1.5" });
    }

    // =========================================================================
    // Audit gap — [ApiVersionNeutral] on controller beats [ApiVersion] on action
    // =========================================================================

    /// <summary>
    /// VersioningNeutralOverridesVersionController carries [ApiVersionNeutral] at class level
    /// and the action adds [ApiVersion("1.0")].
    /// Expected: IsVersionNeutral → true; GetSupportedVersions → empty (neutral wins).
    /// </summary>
    [Fact]
    public void IsVersionNeutral_ControllerNeutral_BeatsByActionVersion_True()
    {
        var controller = Controller("VersioningNeutralOverridesVersion");
        var action = Action(controller, "GET");

        var neutral = ApiVersionExtractor.IsVersionNeutral(controller, action);
        var versions = ApiVersionExtractor.GetSupportedVersions(controller, action);

        neutral.Should().BeTrue("controller-level [ApiVersionNeutral] must override action-level [ApiVersion]");
        versions.Should().BeEmpty("neutral flag suppresses the version list entirely");
    }
}
