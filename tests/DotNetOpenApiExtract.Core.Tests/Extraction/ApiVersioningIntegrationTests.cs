using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Discovery;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Integration tests for API versioning — verifies that <c>x-api-version</c>
/// extensions are correctly emitted on <see cref="OpenApiOperation"/> objects
/// built by <see cref="OpenApiDocumentBuilder"/>.
/// </summary>
public sealed class ApiVersioningIntegrationTests
{
    private readonly OpenApiDocument _document;

    public ApiVersioningIntegrationTests()
    {
        _document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the path that exactly matches <paramref name="path"/> and returns
    /// the operation for the given HTTP method.
    /// </summary>
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

    private static JsonNodeExtension? GetApiVersionExtension(OpenApiOperation operation)
    {
        if (operation.Extensions == null) return null;
        operation.Extensions.TryGetValue("x-api-version", out var ext);
        return ext as JsonNodeExtension;
    }

    // =========================================================================
    // Tests
    // =========================================================================

    /// <summary>
    /// VersionedController.GetAll has [ApiVersion("1.0")] and [ApiVersion("2.0")] on the
    /// controller, no MapToApiVersion on the action.
    /// Expected: x-api-version: ["1.0", "2.0"].
    /// Note: RouteBuilder strips constraints so {version:apiVersion} → {version},
    /// making the path "/api/v{version}/items/all".
    /// </summary>
    [Fact]
    public void VersionedController_ActionHasApiVersionExtension()
    {
        // RouteBuilder normalises {version:apiVersion} → {version}
        var operation = FindOperation("/api/v{version}/items/all", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().NotBeNull("x-api-version extension should be present");

        var arr = ext!.Node.Should().BeOfType<JsonArray>().Subject;
        var values = arr.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo(new[] { "1.0", "2.0" });
    }

    /// <summary>
    /// VersionedController.GetV2Only has [MapToApiVersion("2.0")].
    /// Expected: x-api-version: ["2.0"] (only one element, restricted by MapToApiVersion).
    /// Note: RouteBuilder strips constraints so {id:int} → {id}.
    /// </summary>
    [Fact]
    public void MapToApiVersion_RestrictsToSingleVersion()
    {
        // RouteBuilder normalises {id:int} → {id}; path is "/api/v{version}/items/{id}"
        var operation = FindOperation("/api/v{version}/items/{id}", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().NotBeNull("x-api-version extension should be present");

        var arr = ext!.Node.Should().BeOfType<JsonArray>().Subject;
        var values = arr.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo(new[] { "2.0" });
    }

    /// <summary>
    /// StatusController has [ApiVersionNeutral].
    /// Expected: x-api-version: "neutral" (string, not array).
    /// Note: JsonValue.Create("neutral") returns JsonValuePrimitive which IS a JsonValue;
    /// we use GetValue&lt;string&gt;() to extract the value without depending on concrete type.
    /// </summary>
    [Fact]
    public void ApiVersionNeutral_EmitsNeutralMarker()
    {
        var operation = FindOperation("/api/status", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().NotBeNull("x-api-version extension should be present for neutral controller");

        // JsonValue.Create("neutral") yields JsonValuePrimitive<string> which IS a JsonValue.
        // We verify it's not an array, then read it as a string.
        ext!.Node.Should().NotBeOfType<JsonArray>("neutral marker must be a string, not an array");
        var stringValue = ext.Node.GetValue<string>();
        stringValue.Should().Be("neutral");
    }

    /// <summary>
    /// VersioningStatusSuffixController uses [ApiVersion(1, 0, "beta")] and [ApiVersion(2, 0, "rc1")].
    /// Expected: x-api-version: ["1.0-beta", "2.0-rc1"].
    /// </summary>
    [Fact]
    public void StatusSuffix_IntIntString_EmittedCorrectly()
    {
        var operation = FindOperation("/api/preview/items", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().NotBeNull("x-api-version extension should be present");

        var arr = ext!.Node.Should().BeOfType<JsonArray>().Subject;
        var values = arr.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo(new[] { "1.0-beta", "2.0-rc1" });
    }

    /// <summary>
    /// VersioningDoubleStatusSuffixController uses [ApiVersion(1.0, "alpha")].
    /// Expected: x-api-version: ["1.0-alpha"].
    /// </summary>
    [Fact]
    public void StatusSuffix_DoubleString_EmittedCorrectly()
    {
        var operation = FindOperation("/api/preview2/items", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().NotBeNull("x-api-version extension should be present");

        var arr = ext!.Node.Should().BeOfType<JsonArray>().Subject;
        var values = arr.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo(new[] { "1.0-alpha" });
    }

    /// <summary>
    /// Existing controllers (e.g. UsersController) have no versioning attributes.
    /// Expected: no x-api-version extension on their operations.
    /// </summary>
    [Fact]
    public void NoVersioning_NoExtension()
    {
        // UsersController.GetUsers → GET /api/v1/users
        var operation = FindOperation("/api/v1/users", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().BeNull("non-versioned controllers must not emit x-api-version");
    }

    /// <summary>
    /// VersioningBareDoubleController uses [ApiVersion(1.5)] — the bare double overload.
    /// Expected: x-api-version: ["1.5"].
    /// </summary>
    [Fact]
    public void BareDouble_EmittedCorrectly()
    {
        var operation = FindOperation("/api/doubleversion", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().NotBeNull("x-api-version extension should be present");

        var arr = ext!.Node.Should().BeOfType<JsonArray>().Subject;
        var values = arr.Select(n => n!.GetValue<string>()).ToList();
        values.Should().BeEquivalentTo(new[] { "1.5" });
    }

    /// <summary>
    /// VersioningNeutralOverridesVersionController has [ApiVersionNeutral] on the controller
    /// and [ApiVersion("1.0")] on the action. Neutral must win.
    /// Expected: x-api-version: "neutral" (string, not array).
    /// </summary>
    [Fact]
    public void ControllerNeutral_BeatsActionVersion_EmitsNeutralMarker()
    {
        var operation = FindOperation("/api/neutralwithversion", HttpMethod.Get);

        var ext = GetApiVersionExtension(operation);
        ext.Should().NotBeNull("x-api-version extension must be present when neutral wins");

        ext!.Node.Should().NotBeOfType<JsonArray>("neutral marker must be the string 'neutral', not an array");
        ext.Node.GetValue<string>().Should().Be("neutral");
    }
}
