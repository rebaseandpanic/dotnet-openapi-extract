using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Discovery;

/// <summary>Builds ModernApi once per target version for the whole class.</summary>
public sealed class HttpMethodsFixture
{
    public HttpMethodsFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Documents[version] = VersionedDocumentHarness.SerializeAsync(
                    document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Diagnostics[version] = diagnostics;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];
}

/// <summary>
/// Every way an action declares its HTTP methods yields exactly the (method, route) pairs it
/// declares; actions whose methods cannot be known statically are skipped with a warning.
/// </summary>
public class AcceptVerbsDiscoveryTests(HttpMethodsFixture fixture) : IClassFixture<HttpMethodsFixture>
{
    private static readonly HashSet<string> OperationKeys =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace", "query"];

    public static TheoryData<OpenApiSpecVersion> Versions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(Versions))]
    public void AcceptVerbs_EachMethodBecomesAnOperationOnItsRoute(OpenApiSpecVersion version)
    {
        Methods(version, "/http-methods/multi").Should().BeEquivalentTo(["get", "post"]);
        Methods(version, "/http-methods/single").Should().BeEquivalentTo(["put"]);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void AcceptVerbsWithHttpAttribute_MethodsAreUnitedWithoutDuplicates(OpenApiSpecVersion version)
    {
        Methods(version, "/http-methods/union").Should().BeEquivalentTo(["get", "post"]);
        Methods(version, "/http-methods/same").Should().BeEquivalentTo(["get"]);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EmptyAcceptVerbsAndCustomAttribute_ActionSkipped_NoGuessedMethod(OpenApiSpecVersion version)
    {
        var paths = fixture.Documents[version]["paths"]!.AsObject();

        paths.ContainsKey("/http-methods/empty").Should().BeFalse();
        paths.ContainsKey("/http-methods/custom-query").Should().BeFalse();
        // No method is guessed for the custom attribute: no QUERY anywhere under this controller.
        paths.Where(p => p.Key.StartsWith("/http-methods/", StringComparison.Ordinal))
            .Select(p => p.Value!.AsObject())
            .Should().NotContain(item => item.ContainsKey("query") || item.ContainsKey("x-oai-additionalOperations")
                                         || item.ContainsKey("additionalOperations"));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void SkippedActions_EachGetOneWarning(OpenApiSpecVersion version)
    {
        var diagnostics = fixture.Diagnostics[version];

        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.DiscoveryEmptyAcceptVerbs)
            .Should().ContainSingle()
            .Which.Subjects.Should().Equal("ModernApi.Controllers.HttpMethodsController.Empty");
        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.DiscoveryUnknownHttpMethodAttribute)
            .Should().ContainSingle()
            .Which.Subjects.Should().Equal("ModernApi.Attributes.HttpQueryAttribute");
    }

    private IReadOnlyList<string> Methods(OpenApiSpecVersion version, string path) =>
        fixture.Documents[version]["paths"]![path]!.AsObject()
            .Select(p => p.Key)
            .Where(OperationKeys.Contains)
            .ToList();
}
