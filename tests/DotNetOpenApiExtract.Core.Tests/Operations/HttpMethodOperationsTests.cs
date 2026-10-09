using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Operations;

/// <summary>
/// Builds ModernApi per target version (full and with the conflict prefix excluded), and once
/// with validation, for the whole class.
/// </summary>
public sealed class HttpMethodOperationsFixture
{
    public const string ExcludedPrefix = "/conflicts/excluded";

    public HttpMethodOperationsFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = Build(version, excluded: null);
            Documents[version] = VersionedDocumentHarness.SerializeAsync(
                    document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Diagnostics[version] = diagnostics;
            ExcludedDiagnostics[version] = Build(version, [ExcludedPrefix]).Diagnostics;
        }

        OpenApiDocumentBuilder.BuildWithValidation(
            new OpenApiDocumentOptions { AssemblyPath = TestPaths.ModernApiDll, XmlPath = TestPaths.ModernApiXml },
            new ValidationContext(),
            out var validation);
        Validation = validation;
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> ExcludedDiagnostics { get; } = [];

    public ValidationResult Validation { get; }

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
/// Operations for every HTTP method an action declares, and one deterministic operation when
/// several actions declare the same path and method.
/// </summary>
public class HttpMethodOperationsTests(HttpMethodOperationsFixture fixture) : IClassFixture<HttpMethodOperationsFixture>
{
    public static TheoryData<OpenApiSpecVersion> Versions => [.. VersionedDocumentHarness.Versions];

    private JsonNode Operation(OpenApiSpecVersion version, string path, string method) =>
        fixture.Documents[version]["paths"]![path]![method]!;

    [Theory]
    [MemberData(nameof(Versions))]
    public void Trace_HasItsOwnFieldWithoutWarning(OpenApiSpecVersion version)
    {
        var item = fixture.Documents[version]["paths"]!["/http-methods/trace"]!.AsObject();

        item.ContainsKey("trace").Should().BeTrue();
        item.ContainsKey("x-oai-additionalOperations").Should().BeFalse();
        fixture.Diagnostics[version].Should().NotContain(d => d.Location == "TRACE /http-methods/trace");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void Conflict_KeyWinnerDiscoveredFirst_KeptWithOneWarningNamingBoth(OpenApiSpecVersion version)
    {
        // Key: controller full name, ordinally — ConflictAlpha < ConflictBeta.
        Operation(version, "/conflicts/first-wins", "get")["tags"]!.AsArray()
            .Select(t => t!.GetValue<string>()).Should().Equal("ConflictAlpha");

        fixture.Diagnostics[version]
            .Where(d => d.Code == ExtractionDiagnosticCodes.OperationPathMethodConflict
                        && d.Location == "GET /conflicts/first-wins")
            .Should().ContainSingle()
            .Which.Subjects.Should().Equal(
                "ModernApi.Controllers.ConflictAlphaController.FirstWins",
                "ModernApi.Controllers.ConflictBetaController.FirstWins");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void Conflict_KeyWinnerDiscoveredLast_IsKept(OpenApiSpecVersion version)
    {
        // ConflictYankee < ConflictZulu, although Zulu is discovered first.
        Operation(version, "/conflicts/last-wins", "get")["tags"]!.AsArray()
            .Select(t => t!.GetValue<string>()).Should().Equal("ConflictYankee");
    }

    [Fact]
    public void Validation_PointsAtTheKeptOperationsAction()
    {
        // The kept operation (ConflictYankee) has no summary; the losing action has one.
        fixture.Validation.SkippedRules.Should().NotContain("operation.summary");
        fixture.Validation.Violations
            .Where(v => v.RuleId == "operation.summary" && v.JsonPointer == "#/paths/~1conflicts~1last-wins/get")
            .Should().ContainSingle()
            .Which.Location!.ClassName.Should().Be("ConflictYankeeController");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void Overloads_WinnerByParameterTypeNames(OpenApiSpecVersion version)
    {
        // System.Int32 < System.String ordinally.
        var parameters = Operation(version, "/http-methods/overloads", "get")["parameters"]!.AsArray();

        parameters.Should().ContainSingle();
        parameters[0]!["schema"]!["type"]!.ToJsonString().Should().Contain("integer");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void MultiMethodAction_OperationIdNeverSynthesized(OpenApiSpecVersion version)
    {
        OperationId(version, "/http-methods/operation-id/explicit", "get").Should().Be("SharedExplicitId");
        OperationId(version, "/http-methods/operation-id/explicit", "post").Should().Be("SharedExplicitId");
        OperationId(version, "/http-methods/operation-id/none", "get").Should().BeNull();
        OperationId(version, "/http-methods/operation-id/none", "post").Should().BeNull();
        OperationId(version, "/http-methods/operation-id/named", "get").Should().Be("NamedByAttribute");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void RepeatedMethodAndRoute_KeepsTheGivenName(OpenApiSpecVersion version)
    {
        OperationId(version, "/http-methods/names/merged", "get").Should().Be("MergedName");
        OperationId(version, "/http-methods/names/clash", "get").Should().Be("FirstName");
        OperationId(version, "/http-methods/names/same", "get").Should().Be("SameName");

        var conflicts = fixture.Diagnostics[version]
            .Where(d => d.Code == ExtractionDiagnosticCodes.DiscoveryConflictingOperationNames)
            .ToList();
        conflicts.Should().ContainSingle().Which.Subjects.Should().Equal("FirstName", "SecondName");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void ConflictOnExcludedPath_NotReported(OpenApiSpecVersion version)
    {
        fixture.Diagnostics[version]
            .Should().Contain(d => d.Code == ExtractionDiagnosticCodes.OperationPathMethodConflict
                                   && d.Location == "GET /conflicts/excluded/clash",
                because: "without the exclusion the conflict is reported");
        fixture.ExcludedDiagnostics[version]
            .Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.OperationPathMethodConflict
                                      && d.Location == "GET /conflicts/excluded/clash");
    }

    private string? OperationId(OpenApiSpecVersion version, string path, string method) =>
        Operation(version, path, method)["operationId"]?.GetValue<string>();
}
