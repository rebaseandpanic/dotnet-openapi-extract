using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Security;

/// <summary>ModernApi with a Program.cs declaring a mutualTLS scheme, built for every version and with exclusions.</summary>
public sealed class MutualTlsFixture : IDisposable
{
    private const string Program = """
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("mtls", new OpenApiSecurityScheme { Type = SecuritySchemeType.MutualTLS, Description = "Client certificate" });
            c.AddSecurityDefinition("key", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, Name = "X-Key", In = ParameterLocation.Header });
            c.AddSecurityDefinition("other", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, Name = "X-Other", In = ParameterLocation.Header });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("mtls"), [] }, { new OpenApiSecuritySchemeReference("key"), [] } });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("mtls"), [] } });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("key"), [] }, { new OpenApiSecuritySchemeReference("other"), [] } });
        });
        var app = builder.Build();
        app.MapControllers();
        app.Run();
        """;

    private readonly TempDirectory _directory = new();

    public MutualTlsFixture()
    {
        File.WriteAllText(Path.Combine(_directory.Path, "Program.cs"), Program);
        foreach (var version in VersionedDocumentHarness.Versions)
            Builds[version] = Build(version, null);

        AllExcluded = Build(OpenApiSpecVersion.OpenApi3_0, ["/"]);
        StreamsExcluded = Build(OpenApiSpecVersion.OpenApi3_0, ["/sequential", "/safe-method-body"]);
    }

    private (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(OpenApiSpecVersion version, IReadOnlyList<string>? exclude)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            SourceRoot          = _directory.Path,
            OpenApiVersion      = version,
            OnDiagnostic        = onDiagnostic,
            ExcludePathPrefixes = exclude,
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];

    public (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) AllExcluded { get; }

    public (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) StreamsExcluded { get; }

    public void Dispose() => _directory.Dispose();
}

/// <summary>
/// <c>mutualTLS</c> is written for 3.1+; for 3.0 the scheme and its names in the requirements are
/// removed (the serializer would throw), with one warning that states the change of the auth contract.
/// </summary>
public class MutualTlsTests(MutualTlsFixture fixture) : IClassFixture<MutualTlsFixture>
{
    private const string Removed = ExtractionDiagnosticCodes.SecurityMutualTlsRemoved;
    private const string Location = "#/components/securitySchemes/mtls";

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void From31_TheSchemeAndItsRequirementsAreWritten(OpenApiSpecVersion version)
    {
        var (document, diagnostics) = fixture.Builds[version];

        document["components"]!["securitySchemes"]!["mtls"]!["type"]!.GetValue<string>().Should().Be("mutualTLS");
        JsonNode.DeepEquals(document["security"], JsonNode.Parse("""[{"mtls":[],"key":[]},{"mtls":[]},{"key":[],"other":[]}]"""))
            .Should().BeTrue(document["security"]!.ToJsonString());
        diagnostics.Should().NotContain(d => d.Code == Removed);
    }

    [Fact]
    public void For30_TheSchemeIsRemoved_RequirementsSimplifiedOrGone_OneWarningNamingThem()
    {
        var (document, diagnostics) = fixture.Builds[OpenApiSpecVersion.OpenApi3_0];

        document["components"]!["securitySchemes"]!.AsObject().ContainsKey("mtls").Should().BeFalse();
        JsonNode.DeepEquals(document["security"], JsonNode.Parse("""[{"key":[]},{"key":[],"other":[]}]"""))
            .Should().BeTrue(because: "the AND/OR of the remaining requirements is unchanged: " + document["security"]!.ToJsonString());

        var warning = diagnostics.Where(d => d.Location == Location).Should().ContainSingle().Which;
        warning.Code.Should().Be(Removed);
        warning.Action.Should().Be(DiagnosticAction.SemanticsChanged);
        // The document's requirements, and the operations that copy them to carry their roles.
        string[] Pair(string where) => [$"{where}/0: {{mtls, key}} → {{key}}", $"{where}/1: {{mtls}} → removed"];
        warning.Subjects.Should().Equal(
        [
            "mtls", .. Pair("#/security"), .. Pair("GET /roles/inherited security"), .. Pair("GET /roles/large security"),
            .. Pair("GET /controller-roles/plain security"),
        ]);
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementUndeclaredScheme && d.Subjects.Contains("mtls"),
            because: "the removal is reported once");
    }

    [Fact]
    public void For30_TheWarningIsKept_WhenEveryPathIsExcluded() =>
        fixture.AllExcluded.Diagnostics.Where(d => d.Code == Removed).Should().ContainSingle().Which.Location.Should().Be(Location);

    [Fact]
    public void For30_RemovalMovedQueryAndSourceWarnings_ComposeInOnePass()
    {
        const string query = "#/paths/~1sequential~1query-ndjson/x-oai-additionalOperations/QUERY";
        var diagnostics = fixture.Builds[OpenApiSpecVersion.OpenApi3_0].Diagnostics;

        diagnostics.Should().ContainSingle(d => d.Code == Removed);
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.OperationMovedToAdditionalOperations && d.Location == query);
        diagnostics.Should().Contain(d => d.Code == ExtractionDiagnosticCodes.RequestBodyOnGetHeadDelete
                                          && d.Location != null && d.Location.EndsWith(" /safe-method-body/search", StringComparison.Ordinal));
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.MediaTypeItemSchemaMovedToExtension
                                             && d.Location != null && d.Location.StartsWith(query + "/", StringComparison.Ordinal),
            because: "the item schema lies inside the moved operation");

        var excluded = fixture.StreamsExcluded.Diagnostics;
        excluded.Should().ContainSingle(d => d.Code == Removed);
        excluded.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.OperationMovedToAdditionalOperations && d.Location == query);
        excluded.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.RequestBodyOnGetHeadDelete
                                          && d.Location != null && d.Location.EndsWith(" /safe-method-body/search", StringComparison.Ordinal));
    }
}
