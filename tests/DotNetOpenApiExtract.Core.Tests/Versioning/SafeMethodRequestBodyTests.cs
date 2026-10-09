using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Versioning;

/// <summary>
/// Builds ModernApi once per target version (and once more with an excluded prefix) for the
/// whole class: each build loads the assembly.
/// </summary>
public sealed class SafeMethodBodyFixture
{
    public const string ExcludedPrefix = "/excluded";

    private readonly Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> _diagnostics = [];

    public SafeMethodBodyFixture()
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
            _diagnostics[version] = diagnostics;

            if (version == OpenApiSpecVersion.OpenApi3_0)
                Document30 = VersionedDocumentHarness.SerializeAsync(
                        document, version, DocumentFormat.Json, CancellationToken.None)
                    .GetAwaiter().GetResult();
        }

        Excluded30 = BuildExcluded();
        Excluded30Again = BuildExcluded();
    }

    /// <summary>The ModernApi 3.0 document, serialized.</summary>
    public JsonNode Document30 { get; private set; } = null!;

    /// <summary>Diagnostics of a 3.0 build that excludes <see cref="ExcludedPrefix"/>.</summary>
    public IReadOnlyList<ExtractionDiagnostic> Excluded30 { get; }

    /// <summary>The same build repeated, to compare the order.</summary>
    public IReadOnlyList<ExtractionDiagnostic> Excluded30Again { get; }

    public IReadOnlyList<ExtractionDiagnostic> Diagnostics(OpenApiSpecVersion version) => _diagnostics[version];

    private static IReadOnlyList<ExtractionDiagnostic> BuildExcluded() =>
        VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            OpenApiVersion      = OpenApiSpecVersion.OpenApi3_0,
            ExcludePathPrefixes = [ExcludedPrefix],
            OnDiagnostic        = onDiagnostic,
        }).Diagnostics;
}

/// <summary>
/// A request body on GET, HEAD or DELETE: for a 3.0 target one warning per operation with the
/// method and path, the body itself unchanged; nothing for 3.1/3.2, for POST, or for excluded paths.
/// </summary>
public class SafeMethodRequestBodyTests(SafeMethodBodyFixture fixture) : IClassFixture<SafeMethodBodyFixture>
{
    private const string Path = "/safe-method-body/search";

    [Fact]
    public void Target30_OneWarningPerGetHeadDeleteOperation_NoneForPost()
    {
        RequestBodyWarnings(fixture.Diagnostics(OpenApiSpecVersion.OpenApi3_0))
            .Where(d => d.Location!.EndsWith(" " + Path, StringComparison.Ordinal))
            .Select(d => d.Location)
            .Should().BeEquivalentTo([$"GET {Path}", $"HEAD {Path}", $"DELETE {Path}"]);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("head")]
    [InlineData("delete")]
    public void Target30_RequestBodyIsKept(string method)
    {
        var body = fixture.Document30["paths"]![Path]![method]!["requestBody"];

        body.Should().NotBeNull();
        body!["required"]!.GetValue<bool>().Should().BeTrue();
        body["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
            .Should().Be("#/components/schemas/SearchFilter");
        JsonNode.DeepEquals(body, fixture.Document30["paths"]![Path]!["post"]!["requestBody"]).Should().BeTrue();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void Target31And32_NoWarnings(OpenApiSpecVersion version)
    {
        RequestBodyWarnings(fixture.Diagnostics(version)).Should().BeEmpty();
    }

    [Fact]
    public void ExcludedPath_NoWarning_AndOrderIsStable()
    {
        RequestBodyWarnings(fixture.Diagnostics(OpenApiSpecVersion.OpenApi3_0))
            .Should().Contain(d => d.Location == $"GET {SafeMethodBodyFixture.ExcludedPrefix}{Path}",
                because: "without the exclusion the excluded-prefix operation is warned about too");

        RequestBodyWarnings(fixture.Excluded30)
            .Should().NotContain(d => d.Location!.Contains(SafeMethodBodyFixture.ExcludedPrefix, StringComparison.Ordinal));
        fixture.Excluded30.Should().NotBeEmpty();
        fixture.Excluded30Again.Select(d => (d.Code, d.Location, d.Message))
            .Should().Equal(fixture.Excluded30.Select(d => (d.Code, d.Location, d.Message)));
    }

    private static IReadOnlyList<ExtractionDiagnostic> RequestBodyWarnings(IEnumerable<ExtractionDiagnostic> diagnostics) =>
        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.RequestBodyOnGetHeadDelete).ToList();
}
