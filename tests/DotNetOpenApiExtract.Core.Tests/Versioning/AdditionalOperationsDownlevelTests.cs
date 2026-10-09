using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Versioning;

/// <summary>Builds ModernApi per target version, and for 3.0 with one prefix excluded, for the whole class.</summary>
public sealed class AdditionalOperationsFixture
{
    public const string ExcludedPrefix = "/excluded-additional";

    public AdditionalOperationsFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = Build(version, null);
            Documents[version] = VersionedDocumentHarness.SerializeAsync(
                    document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Diagnostics[version] = diagnostics;
        }

        Excluded30 = Build(OpenApiSpecVersion.OpenApi3_0, [ExcludedPrefix]).Diagnostics;
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];

    public IReadOnlyList<ExtractionDiagnostic> Excluded30 { get; }

    private static (OpenApiDocument Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(
        OpenApiSpecVersion version, IReadOnlyList<string>? excluded) =>
        VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            OpenApiVersion      = version,
            ExcludePathPrefixes = excluded,
            OnDiagnostic        = onDiagnostic,
        });
}

/// <summary>
/// QUERY and non-standard methods: 3.2 writes them natively (<c>query</c>, <c>additionalOperations</c>);
/// 3.0/3.1 get them under <c>x-oai-additionalOperations</c> with exactly one warning per moved
/// operation, located by the pointer of the actual output.
/// </summary>
public class AdditionalOperationsDownlevelTests(AdditionalOperationsFixture fixture) : IClassFixture<AdditionalOperationsFixture>
{
    private const string Moved = ExtractionDiagnosticCodes.OperationMovedToAdditionalOperations;

    public static TheoryData<OpenApiSpecVersion> Downlevel => [OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1];

    private JsonObject Item(OpenApiSpecVersion version, string path) =>
        fixture.Documents[version]["paths"]![path]!.AsObject();

    private static string ExtensionPointer(string path, string method) =>
        $"#/paths/{path.Replace("~", "~0").Replace("/", "~1")}/x-oai-additionalOperations/{method}";

    /// <summary>Records located at <paramref name="pointer"/> or inside it (whole-segment match).</summary>
    private static IEnumerable<ExtractionDiagnostic> AtOrUnder(IEnumerable<ExtractionDiagnostic> diagnostics, string pointer) =>
        diagnostics.Where(d => d.Location == pointer || d.Location?.StartsWith(pointer + "/", StringComparison.Ordinal) == true);

    [Fact]
    public void Query_Target32_NativeFieldWithoutWarning()
    {
        var item = Item(OpenApiSpecVersion.OpenApi3_2, "/additional-methods/search");

        item.ContainsKey("query").Should().BeTrue();
        (item["additionalOperations"]?.AsObject().ContainsKey("QUERY") ?? false).Should().BeFalse();
        fixture.Diagnostics[OpenApiSpecVersion.OpenApi3_2].Should().NotContain(d => d.Code == Moved);
    }

    [Theory]
    [MemberData(nameof(Downlevel))]
    public void Query_Downlevel_MovedToExtensionWithOneWarning(OpenApiSpecVersion version)
    {
        const string path = "/additional-methods/search";
        var item = Item(version, path);
        var pointer = ExtensionPointer(path, "QUERY");

        item.ContainsKey("query").Should().BeFalse();
        item["x-oai-additionalOperations"]!["QUERY"]!["responses"].Should().NotBeNull();

        var warning = fixture.Diagnostics[version].Where(d => d.Code == Moved && d.Location == pointer)
            .Should().ContainSingle().Subject;
        warning.Action.Should().Be(DiagnosticAction.MovedToExtension);
        warning.ExtensionName.Should().Be("x-oai-additionalOperations");
        warning.RequiredVersion.Should().Be(OpenApiSpecVersion.OpenApi3_2);
        warning.TargetVersion.Should().Be(version);
        JsonReferences.Resolve(fixture.Documents[version], pointer).Should().NotBeNull();
        AtOrUnder(fixture.Diagnostics[version], pointer).Where(d => d.Location != pointer)
            .Should().BeEmpty(because: "the moved operation's subtree gets no warnings of its own");
    }

    [Theory]
    [InlineData("/additional-methods/link", "LINK")]
    [InlineData("/additional-methods/purge", "Purge")]
    public void NonStandardMethod_KeyKeepsLiteralCapitalization(string path, string method)
    {
        Item(OpenApiSpecVersion.OpenApi3_2, path)["additionalOperations"]!.AsObject()
            .Select(p => p.Key).Should().Equal(method);
        fixture.Diagnostics[OpenApiSpecVersion.OpenApi3_2].Should().NotContain(d => d.Code == Moved);

        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_0, OpenApiSpecVersion.OpenApi3_1 })
        {
            Item(version, path)["x-oai-additionalOperations"]!.AsObject()
                .Select(p => p.Key).Should().Equal(method);
            fixture.Diagnostics[version].Where(d => d.Code == Moved && d.Location == ExtensionPointer(path, method))
                .Should().ContainSingle();
        }
    }

    [Theory]
    [MemberData(nameof(Downlevel))]
    public void TwoQueryActionsOnOnePath_Downlevel_ConflictAndOneMoveWarning(OpenApiSpecVersion version)
    {
        var pointer = ExtensionPointer("/additional-methods/clash", "QUERY");
        var diagnostics = fixture.Diagnostics[version];

        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.OperationPathMethodConflict && d.Location == pointer)
            .Should().ContainSingle()
            .Which.Subjects.Should().Equal(
                "ModernApi.Controllers.AdditionalMethodsController.ClashA",
                "ModernApi.Controllers.AdditionalMethodsController.ClashB");
        diagnostics.Where(d => d.Code == Moved && d.Location == pointer).Should().ContainSingle();
    }

    [Fact]
    public void TwoQueryActionsOnOnePath_Target32_ConflictOnly()
    {
        var diagnostics = fixture.Diagnostics[OpenApiSpecVersion.OpenApi3_2];

        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.OperationPathMethodConflict
                               && d.Location == "QUERY /additional-methods/clash")
            .Should().ContainSingle();
        diagnostics.Should().NotContain(d => d.Code == Moved);
    }

    [Fact]
    public void QueryOnTwoPaths_TwoWarnings_ExcludedPath_None()
    {
        var full = fixture.Diagnostics[OpenApiSpecVersion.OpenApi3_0];
        full.Where(d => d.Code == Moved && d.Location == ExtensionPointer("/additional-methods/search", "QUERY")).Should().ContainSingle();
        full.Where(d => d.Code == Moved && d.Location == ExtensionPointer("/additional-methods/search-two", "QUERY")).Should().ContainSingle();
        full.Where(d => d.Code == Moved && d.Location == ExtensionPointer("/excluded-additional/search", "QUERY")).Should().ContainSingle(
            because: "without the exclusion the excluded-prefix operation is reported too");

        AtOrUnder(fixture.Excluded30, ExtensionPointer("/excluded-additional/search", "QUERY")).Should().BeEmpty();
    }
}
