using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Stage;

/// <summary>
/// Builds ModernApi once per version — in full and with part of its controllers excluded — and
/// SampleApi once for 3.0, for the whole class.
/// </summary>
public sealed class JointVersionMatrixFixture
{
    /// <summary>Prefixes excluded in the partial builds: one with warnings, one with its own components.</summary>
    public static readonly IReadOnlyList<string> ExcludedPrefixes = ["/excluded", "/component-ids"];

    public JointVersionMatrixFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            Full[version]     = Build(version, excludedPrefixes: null);
            Excluded[version] = Build(version, ExcludedPrefixes);
        }

        SampleApi30 = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            OnDiagnostic = onDiagnostic,
        }).Diagnostics;
    }

    public Dictionary<OpenApiSpecVersion, BuiltDocument> Full { get; } = [];

    public Dictionary<OpenApiSpecVersion, BuiltDocument> Excluded { get; } = [];

    public IReadOnlyList<ExtractionDiagnostic> SampleApi30 { get; }

    private static BuiltDocument Build(OpenApiSpecVersion version, IReadOnlyList<string>? excludedPrefixes)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            OpenApiVersion      = version,
            ExcludePathPrefixes = excludedPrefixes,
            OnDiagnostic        = onDiagnostic,
        });
        return new BuiltDocument(document, diagnostics);
    }
}

/// <summary>A built document with the diagnostics of its build.</summary>
public sealed record BuiltDocument(OpenApiDocument Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics);

/// <summary>
/// Every construct of the ModernApi fixture, in every target version and format: serialization
/// succeeds, references resolve, each warning is reported once per place, and nothing is reported
/// for excluded paths or for components only they reach.
/// </summary>
public class JointVersionMatrixTests(JointVersionMatrixFixture fixture) : IClassFixture<JointVersionMatrixFixture>
{
    public static TheoryData<OpenApiSpecVersion, DocumentFormat> VersionsAndFormats
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, DocumentFormat>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                data.Add(version, DocumentFormat.Json);
                data.Add(version, DocumentFormat.Yaml);
            }
            return data;
        }
    }

    public static TheoryData<OpenApiSpecVersion> Versions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(VersionsAndFormats))]
    public async Task ModernApi_SerializesAndEveryReferenceResolves(OpenApiSpecVersion version, DocumentFormat format)
    {
        var root = await VersionedDocumentHarness.SerializeAsync(
            fixture.Full[version].Document, version, format, TestContext.Current.CancellationToken);

        var references = JsonReferences.All(root).Distinct().ToList();
        references.Should().NotBeEmpty();
        references.Where(r => JsonReferences.Resolve(root, r) == null)
            .Should().BeEmpty(because: "every $ref must point into the same document");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void ModernApi_EachVersionFeatureLocationReportedAtMostOnce(OpenApiSpecVersion version)
    {
        StageDiagnostics(fixture.Full[version].Diagnostics)
            .GroupBy(d => (d.TargetVersion, d.Feature, d.Location))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task ModernApi_ExcludedPaths_LoseExactlyTheirOwnWarnings(OpenApiSpecVersion version)
    {
        var excluded = fixture.Excluded[version];
        var root = await VersionedDocumentHarness.SerializeAsync(
            excluded.Document, version, DocumentFormat.Json, TestContext.Current.CancellationToken);
        var onlyFromExcluded = ComponentsUnreachableFromKeptPaths(root);

        bool BelongsToExcluded(ExtractionDiagnostic d) =>
            d.Location is { } location
            && (JointVersionMatrixFixture.ExcludedPrefixes.Any(prefix => OperationPath(location)?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true
                                                                        || location.StartsWith("#/paths/" + prefix.Replace("/", "~1"), StringComparison.Ordinal))
                || onlyFromExcluded.Any(id => location.StartsWith($"#/components/schemas/{id}", StringComparison.Ordinal)));

        var expected = fixture.Full[version].Diagnostics.Where(d => !BelongsToExcluded(d)).Select(Identity).ToList();

        excluded.Diagnostics.Where(BelongsToExcluded).Should().BeEmpty();
        excluded.Diagnostics.Select(Identity).Should().Equal(expected,
            because: "records elsewhere, including document-level ones, are kept in the same number and order");
    }

    [Fact]
    public void ModernApi_Target30_HasRecordsUnderAnExcludedPrefix()
    {
        // Guard for the test above: the full 3.0 build must have something to lose.
        fixture.Full[OpenApiSpecVersion.OpenApi3_0].Diagnostics
            .Select(d => OperationPath(d.Location))
            .Should().Contain(path => path != null
                                      && path.StartsWith(JointVersionMatrixFixture.ExcludedPrefixes[0], StringComparison.Ordinal));
    }

    [Fact]
    public void SampleApi_Target30_HasNoStageWarnings()
    {
        StageDiagnostics(fixture.SampleApi30).Should().BeEmpty();
    }

    /// <summary>Warnings of the version-aware stage carry a target version; earlier warnings do not.</summary>
    private static IEnumerable<ExtractionDiagnostic> StageDiagnostics(IEnumerable<ExtractionDiagnostic> diagnostics) =>
        diagnostics.Where(d => d.TargetVersion != null);

    private static (string Code, string? Location, string Message) Identity(ExtractionDiagnostic d) =>
        (d.Code, d.Location, d.Message);

    /// <summary>The path of a <c>METHOD /path</c> location, or null for other locations.</summary>
    private static string? OperationPath(string? location)
    {
        if (location == null || location.StartsWith('#'))
            return null;

        var space = location.IndexOf(' ');
        return space > 0 ? location[(space + 1)..] : null;
    }

    /// <summary>Component schemas no kept path reaches, directly or through other components.</summary>
    private static IReadOnlySet<string> ComponentsUnreachableFromKeptPaths(JsonNode root)
    {
        const string prefix = "#/components/schemas/";
        var schemas = root["components"]?["schemas"]?.AsObject();
        if (schemas == null)
            return new HashSet<string>();

        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(JsonReferences.All(root["paths"]).Where(r => r.StartsWith(prefix, StringComparison.Ordinal)));
        while (pending.TryDequeue(out var reference))
        {
            var id = reference[prefix.Length..];
            if (!reached.Add(id))
                continue;
            foreach (var nested in JsonReferences.All(schemas[id]).Where(r => r.StartsWith(prefix, StringComparison.Ordinal)))
                pending.Enqueue(nested);
        }

        return schemas.Select(s => s.Key).Where(id => !reached.Contains(id)).ToHashSet(StringComparer.Ordinal);
    }
}
