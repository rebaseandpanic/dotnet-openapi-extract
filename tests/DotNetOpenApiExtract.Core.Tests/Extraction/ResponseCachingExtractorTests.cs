using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Loading;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="ResponseCachingExtractor"/>.
/// Tests load SampleApi.dll and exercise attribute extraction from the
/// <c>CachingController</c> fixture.
/// </summary>
public class ResponseCachingExtractorTests : IDisposable
{
    private readonly AssemblyLoader _loader;
    private readonly ControllerInfo _cachingController;
    private readonly ActionInfo _getCachedAction;        // [ResponseCache(Duration=60, Location=Any)]
    private readonly ActionInfo _getNoStoreAction;       // [ResponseCache(NoStore=true)]
    private readonly ActionInfo _getOutputCachedAction;  // [OutputCache(Duration=30)]
    private readonly ControllerInfo _usersController;
    private readonly ActionInfo _getUsersAction;         // no caching attributes

    private readonly ActionInfo _getNoStoreWithDurationAction; // [ResponseCache(NoStore=true, Duration=30)]
    private readonly ActionInfo _getLocationNoneAction;        // [ResponseCache(Duration=60, Location=None)]

    public ResponseCachingExtractorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
        var controllers = ControllerDiscovery.DiscoverControllers(_loader.Assembly);

        _cachingController = controllers.Single(c => c.Name == "Caching");
        _usersController   = controllers.Single(c => c.Name == "Users");

        var cachingActions    = ActionDiscovery.DiscoverActions(_cachingController);
        _getCachedAction      = cachingActions.Single(a => a.Name == "GetCached");
        _getNoStoreAction     = cachingActions.Single(a => a.Name == "GetNoStore");
        _getOutputCachedAction = cachingActions.Single(a => a.Name == "GetOutputCached");
        _getNoStoreWithDurationAction = cachingActions.Single(a => a.Name == "GetNoStoreWithDuration");
        _getLocationNoneAction        = cachingActions.Single(a => a.Name == "GetLocationNone");

        var usersActions  = ActionDiscovery.DiscoverActions(_usersController);
        _getUsersAction   = usersActions.Single(a => a.Name == "GetUsers");
    }

    public void Dispose() => _loader.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // 6. [ResponseCache] with Duration returns DurationSeconds
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ResponseCacheAttribute_Duration_Returned()
    {
        var info = ResponseCachingExtractor.Extract(_cachingController, _getCachedAction);

        info.Should().NotBeNull();
        info!.DurationSeconds.Should().Be(60);
        info.Source.Should().Be("ResponseCache");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. [ResponseCache] with all named args
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ResponseCacheAttribute_AllNamedArgs_Returned()
    {
        // [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept-Encoding")]
        var info = ResponseCachingExtractor.Extract(_cachingController, _getCachedAction);

        info.Should().NotBeNull();
        info!.DurationSeconds.Should().Be(60);
        info.Location.Should().Be("Any");
        info.NoStore.Should().BeFalse();
        info.VaryByHeader.Should().Be("Accept-Encoding");
        info.Source.Should().Be("ResponseCache");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. [OutputCache] with Duration returns DurationSeconds
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_OutputCacheAttribute_Duration_Returned()
    {
        var info = ResponseCachingExtractor.Extract(_cachingController, _getOutputCachedAction);

        info.Should().NotBeNull();
        info!.DurationSeconds.Should().Be(30);
        info.Source.Should().Be("OutputCache");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Action-level attribute overrides controller-level
    // CachingController has [ResponseCache(Duration = 999)] at controller level.
    // GetCached has [ResponseCache(Duration = 60)] at action level — must win.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ActionOverridesController()
    {
        // Controller has [ResponseCache(Duration = 999)]; action has Duration = 60.
        // Action-level must take precedence.
        var info = ResponseCachingExtractor.Extract(_cachingController, _getCachedAction);

        info.Should().NotBeNull(
            because: "action-level [ResponseCache] should be extracted");
        info!.DurationSeconds.Should().Be(60,
            because: "action-level Duration=60 must override controller-level Duration=999");
        info.Source.Should().Be("ResponseCache");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. NoStore = true extracted correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ResponseCacheNoStore_NoStoreIsTrue()
    {
        var info = ResponseCachingExtractor.Extract(_cachingController, _getNoStoreAction);

        info.Should().NotBeNull();
        info!.NoStore.Should().BeTrue();
        info.Source.Should().Be("ResponseCache");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. No attributes → returns null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NoAttributes_ReturnsNull()
    {
        var info = ResponseCachingExtractor.Extract(_usersController, _getUsersAction);

        info.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. [ResponseCache(NoStore = true, Duration = 30)] — both NoStore and Duration
    //     extracted; BuildCacheControlDescription emits "no-store, max-age=30"
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NoStoreTrueAndDuration_BothExtracted()
    {
        var info = ResponseCachingExtractor.Extract(_cachingController, _getNoStoreWithDurationAction);

        info.Should().NotBeNull();
        info!.NoStore.Should().BeTrue(
            because: "NoStore=true must be captured even when Duration is also set");
        info.DurationSeconds.Should().Be(30,
            because: "Duration=30 must be captured even when NoStore is also set");
        info.Source.Should().Be("ResponseCache");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 13. [ResponseCache(Duration = 60, Location = ResponseCacheLocation.None)]
    //     Location="None" is extracted; BuildCacheControlDescription emits "no-cache"
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_LocationNone_ExtractedAsNone()
    {
        var info = ResponseCachingExtractor.Extract(_cachingController, _getLocationNoneAction);

        info.Should().NotBeNull();
        info!.Location.Should().Be("None",
            because: "ResponseCacheLocation.None should be decoded as the string \"None\"");
        info.DurationSeconds.Should().Be(60);
        info.Source.Should().Be("ResponseCache");
    }
}
