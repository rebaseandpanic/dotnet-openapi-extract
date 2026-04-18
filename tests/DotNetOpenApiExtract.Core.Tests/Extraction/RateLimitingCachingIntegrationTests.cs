using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Integration tests for rate-limiting and response-caching attribute extraction.
/// Verifies that <c>x-rate-limit-policy</c>, <c>x-rate-limit-disabled</c> extensions,
/// and <c>Cache-Control</c> response headers are correctly emitted on
/// <see cref="OpenApiOperation"/> objects built by <see cref="OpenApiDocumentBuilder"/>.
/// </summary>
public sealed class RateLimitingCachingIntegrationTests
{
    private readonly OpenApiDocument _document;

    public RateLimitingCachingIntegrationTests()
    {
        _document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private OpenApiOperation FindOperation(string path, HttpMethod httpMethod)
    {
        if (!_document.Paths.TryGetValue(path, out var pathItemInterface))
            throw new InvalidOperationException(
                $"Path '{path}' not found. Available: {string.Join(", ", _document.Paths.Keys.OrderBy(p => p))}");

        var pathItem = pathItemInterface as OpenApiPathItem
            ?? throw new InvalidOperationException($"Path item for '{path}' is not an OpenApiPathItem.");

        if (pathItem.Operations == null || !pathItem.Operations.TryGetValue(httpMethod, out var operation))
            throw new InvalidOperationException(
                $"No {httpMethod.Method} operation found at path '{path}'.");

        return operation;
    }

    private static JsonNodeExtension? GetExtension(OpenApiOperation operation, string key)
    {
        if (operation.Extensions == null) return null;
        operation.Extensions.TryGetValue(key, out var ext);
        return ext as JsonNodeExtension;
    }

    // =========================================================================
    // Rate limiting tests
    // =========================================================================

    /// <summary>
    /// RateLimitingController.Get inherits [EnableRateLimiting("default-policy")] from the controller.
    /// Expected: x-rate-limit-policy: "default-policy".
    /// </summary>
    [Fact]
    public void Build_RateLimitingController_ActionsHaveExtension()
    {
        var operation = FindOperation("/api/rl", HttpMethod.Get);

        var ext = GetExtension(operation, "x-rate-limit-policy");
        ext.Should().NotBeNull(because: "action should have x-rate-limit-policy extension");
        ext!.Node.Should().NotBeNull();
        ext.Node!.GetValue<string>().Should().Be("default-policy");
    }

    /// <summary>
    /// RateLimitingController.GetOverride has [EnableRateLimiting("strict")].
    /// Expected: x-rate-limit-policy: "strict" (overrides controller-level "default-policy").
    /// </summary>
    [Fact]
    public void Build_RateLimitingOverride_ActionHasOwnPolicy()
    {
        var operation = FindOperation("/api/rl/override", HttpMethod.Get);

        var ext = GetExtension(operation, "x-rate-limit-policy");
        ext.Should().NotBeNull();
        ext!.Node!.GetValue<string>().Should().Be("strict");
    }

    /// <summary>
    /// RateLimitingController.GetDisabled has [DisableRateLimiting].
    /// Expected: x-rate-limit-disabled: true.
    /// </summary>
    [Fact]
    public void Build_RateLimitingDisabled_ActionHasDisabledMarker()
    {
        var operation = FindOperation("/api/rl/disabled", HttpMethod.Get);

        var ext = GetExtension(operation, "x-rate-limit-disabled");
        ext.Should().NotBeNull(because: "action should have x-rate-limit-disabled extension");
        ext!.Node!.GetValue<bool>().Should().BeTrue();
    }

    /// <summary>
    /// A controller without rate-limiting attributes (UsersController) should have no
    /// x-rate-limit-policy or x-rate-limit-disabled extension.
    /// </summary>
    [Fact]
    public void Build_NonRateLimitedController_NoExtension()
    {
        var operation = FindOperation("/api/v1/users", HttpMethod.Get);

        // When no rate-limiting attributes are present, Extensions is either null
        // or does not contain rate-limiting keys.
        var hasPolicyKey = operation.Extensions != null
            && operation.Extensions.ContainsKey("x-rate-limit-policy");
        var hasDisabledKey = operation.Extensions != null
            && operation.Extensions.ContainsKey("x-rate-limit-disabled");

        hasPolicyKey.Should().BeFalse(
            because: "UsersController has no rate-limiting attributes");
        hasDisabledKey.Should().BeFalse(
            because: "UsersController has no rate-limiting attributes");
    }

    // =========================================================================
    // Response caching tests
    // =========================================================================

    /// <summary>
    /// CachingController.GetCached has [ResponseCache(Duration = 60, Location = Any)].
    /// Expected: Cache-Control header on the 200 response with max-age=60.
    /// </summary>
    [Fact]
    public void Build_ResponseCacheDuration_CacheControlHeaderInResponse()
    {
        var operation = FindOperation("/api/cache/response-cache", HttpMethod.Get);

        operation.Responses.Should().ContainKey("200");
        var response = operation.Responses["200"] as OpenApiResponse;
        response.Should().NotBeNull();
        response!.Headers.Should().ContainKey("Cache-Control");

        var header = response.Headers["Cache-Control"] as OpenApiHeader;
        header.Should().NotBeNull();
        header!.Description.Should().Contain("max-age=60");
    }

    /// <summary>
    /// CachingController.GetNoStore has [ResponseCache(NoStore = true)].
    /// Expected: Cache-Control header on the 200 response with no-store.
    /// </summary>
    [Fact]
    public void Build_NoStore_CacheControlDescription()
    {
        var operation = FindOperation("/api/cache/no-store", HttpMethod.Get);

        operation.Responses.Should().ContainKey("200");
        var response = operation.Responses["200"] as OpenApiResponse;
        response.Should().NotBeNull();
        response!.Headers.Should().ContainKey("Cache-Control");

        var header = response.Headers["Cache-Control"] as OpenApiHeader;
        header.Should().NotBeNull();
        header!.Description.Should().Contain("no-store");
    }

    /// <summary>
    /// CachingController.GetClientCached has [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Client)].
    /// Expected: Cache-Control header on the 200 response containing "private" and "max-age=30".
    /// </summary>
    [Fact]
    public void ResponseCache_LocationClient_EmitsCachePrivateDirective()
    {
        var operation = FindOperation("/api/cache/client-cache", HttpMethod.Get);

        operation.Responses.Should().ContainKey("200");
        var response = operation.Responses["200"] as OpenApiResponse;
        response.Should().NotBeNull();
        response!.Headers.Should().ContainKey("Cache-Control");

        var header = response.Headers["Cache-Control"] as OpenApiHeader;
        header.Should().NotBeNull();
        header!.Description.Should().Contain("private",
            because: "ResponseCacheLocation.Client maps to the HTTP Cache-Control 'private' directive");
        header.Description.Should().Contain("max-age=30",
            because: "Duration=30 should emit max-age=30");
    }

    /// <summary>
    /// CachingController.GetNoStoreWithDuration has [ResponseCache(NoStore = true, Duration = 30)].
    /// Both conflicting directives must appear: "no-store" and "max-age=30" in Cache-Control.
    /// This exercises the branch in BuildCacheControlDescription that emits both when both are set.
    /// </summary>
    [Fact]
    public void ResponseCache_NoStoreWithDuration_EmitsBothDirectives()
    {
        var operation = FindOperation("/api/cache/no-store-with-duration", HttpMethod.Get);

        operation.Responses.Should().ContainKey("200");
        var response = operation.Responses["200"] as OpenApiResponse;
        response.Should().NotBeNull();
        response!.Headers.Should().ContainKey("Cache-Control");

        var header = response.Headers["Cache-Control"] as OpenApiHeader;
        header.Should().NotBeNull();
        header!.Description.Should().Contain("no-store",
            because: "NoStore=true must emit the no-store directive");
        header.Description.Should().Contain("max-age=30",
            because: "Duration=30 must emit max-age=30 even when NoStore is also set");
    }

    /// <summary>
    /// CachingController.GetLocationNone has [ResponseCache(Duration = 60, Location = ResponseCacheLocation.None)].
    /// Expected: Cache-Control header containing "no-cache" and "max-age=60".
    /// This exercises the Location=None branch in BuildCacheControlDescription.
    /// </summary>
    [Fact]
    public void ResponseCache_LocationNone_EmitsNoCacheDirective()
    {
        var operation = FindOperation("/api/cache/location-none", HttpMethod.Get);

        operation.Responses.Should().ContainKey("200");
        var response = operation.Responses["200"] as OpenApiResponse;
        response.Should().NotBeNull();
        response!.Headers.Should().ContainKey("Cache-Control");

        var header = response.Headers["Cache-Control"] as OpenApiHeader;
        header.Should().NotBeNull();
        header!.Description.Should().Contain("no-cache",
            because: "ResponseCacheLocation.None maps to the HTTP Cache-Control 'no-cache' directive");
        header.Description.Should().Contain("max-age=60",
            because: "Duration=60 should emit max-age=60");
    }

    /// <summary>
    /// RateLimitingController.GetDisableWins has both [DisableRateLimiting] and
    /// [EnableRateLimiting("strict")] on the same action.
    /// [DisableRateLimiting] must win unconditionally.
    /// Expected: x-rate-limit-disabled=true; no x-rate-limit-policy extension.
    /// </summary>
    [Fact]
    public void Build_DisableAndEnableOnSameAction_DisableWins()
    {
        var operation = FindOperation("/api/rl/disable-wins", HttpMethod.Get);

        var disabledExt = GetExtension(operation, "x-rate-limit-disabled");
        disabledExt.Should().NotBeNull(
            because: "[DisableRateLimiting] must set x-rate-limit-disabled when both attributes are present");
        disabledExt!.Node!.GetValue<bool>().Should().BeTrue();

        var policyExt = GetExtension(operation, "x-rate-limit-policy");
        policyExt.Should().BeNull(
            because: "x-rate-limit-policy must not be set when [DisableRateLimiting] wins");
    }
}
