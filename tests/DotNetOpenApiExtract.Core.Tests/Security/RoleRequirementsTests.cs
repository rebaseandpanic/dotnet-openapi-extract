using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Security;

/// <summary>ModernApi with Program.cs schemes and document requirements, in every version, plus variants.</summary>
public sealed class RoleRequirementsFixture : IDisposable
{
    private const string Schemes = """
        c.AddSecurityDefinition("key", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, Name = "X-Key", In = ParameterLocation.Header });
        c.AddSecurityDefinition("bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer" });
        c.AddSecurityDefinition("oauth", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows { ClientCredentials = new OpenApiOAuthFlow { TokenUrl = new Uri("https://auth.example.com/token"), Scopes = new Dictionary<string, string>() } },
        });
        """;

    private readonly TempDirectory _withRequirements = new();
    private readonly TempDirectory _withoutRequirements = new();

    public RoleRequirementsFixture()
    {
        File.WriteAllText(Path.Combine(_withRequirements.Path, "Program.cs"), $$"""
            builder.Services.AddSwaggerGen(c =>
            {
                {{Schemes}}
                c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("key"), [] } });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("bearer"), [] } });
            });
            """);
        File.WriteAllText(Path.Combine(_withoutRequirements.Path, "Program.cs"), $$"""
            builder.Services.AddSwaggerGen(c =>
            {
                {{Schemes}}
            });
            """);

        foreach (var version in VersionedDocumentHarness.Versions)
            Builds[version] = Build(_withRequirements, version, null);
        Excluded = Build(_withRequirements, OpenApiSpecVersion.OpenApi3_0, ["/roles", "/controller-roles"]);
        NoRequirements = Build(_withoutRequirements, OpenApiSpecVersion.OpenApi3_1, null);
    }

    private static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(
        TempDirectory source, OpenApiSpecVersion version, IReadOnlyList<string>? exclude)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            SourceRoot          = source.Path,
            OpenApiVersion      = version,
            OnDiagnostic        = onDiagnostic,
            ExcludePathPrefixes = exclude,
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    public Dictionary<OpenApiSpecVersion, (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics)> Builds { get; } = [];

    public (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Excluded { get; }

    public (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) NoRequirements { get; }

    public void Dispose()
    {
        _withRequirements.Dispose();
        _withoutRequirements.Dispose();
    }
}

/// <summary>
/// <c>[Authorize(Roles)]</c>: roles of one attribute are OR, attributes are AND; they go into the values
/// of the effective non-OAuth schemes for 3.1+ (one requirement object per alternative, written in
/// full), never into OAuth scopes; what the document cannot carry is reported per operation.
/// </summary>
public class RoleRequirementsTests(RoleRequirementsFixture fixture) : IClassFixture<RoleRequirementsFixture>
{
    private const string NotWritten = ExtractionDiagnosticCodes.SecurityRolesNotWritten;

    private static JsonNode? Security(JsonNode document, string path, string method = "get") =>
        document["paths"]![path]![method]!["security"];

    private static bool Same(JsonNode? actual, string expected) => JsonNode.DeepEquals(actual, JsonNode.Parse(expected));

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void Roles_KeepTheirOrAndAnd_InTheRequirements(OpenApiSpecVersion version)
    {
        var document = fixture.Builds[version].Document;

        Same(Security(document, "/roles/or"), """[{"key":["A"]},{"key":["B"]}]""").Should().BeTrue(Security(document, "/roles/or")!.ToJsonString());
        Same(Security(document, "/controller-roles/and"), """[{"key":["A","C"]},{"key":["A","D"]}]""").Should().BeTrue();
        Same(Security(document, "/controller-roles/plain"), """[{"key":["A"]},{"bearer":["A"]}]""").Should().BeTrue(
            because: "roles only on the controller apply to its operations");
        Same(Security(document, "/roles/inherited"), """[{"key":["A"]},{"bearer":["A"]}]""").Should().BeTrue(
            because: "the document's requirements are copied onto the operation with the roles");
        Same(Security(document, "/roles/anonymous"), "[]").Should().BeTrue();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void ManyAlternatives_AreWrittenInFull(OpenApiSpecVersion version)
    {
        var security = Security(fixture.Builds[version].Document, "/roles/large")!.AsArray();

        security.Should().HaveCount(54, because: "3 × 3 × 3 role alternatives × 2 document requirements");
        security.Select(r => r!.ToJsonString()).Should().OnlyHaveUniqueItems();
        security[0]!.ToJsonString().Should().Be("""{"key":["A1","B1","C1"]}""");
        security[53]!.ToJsonString().Should().Be("""{"bearer":["A3","B3","C3"]}""");
    }

    [Fact]
    public void For30_ValuesAreEmpty_OneWarningPerOperation_NoneOnExcludedPaths()
    {
        var (document, diagnostics) = fixture.Builds[OpenApiSpecVersion.OpenApi3_0];

        Same(Security(document, "/roles/or"), """[{"key":[]}]""").Should().BeTrue();
        Same(Security(document, "/roles/inherited"), """[{"key":[]},{"bearer":[]}]""").Should().BeTrue();

        var warning = diagnostics.Where(d => d.Location == "GET /roles/or" && d.Code == NotWritten).Should().ContainSingle().Which;
        warning.Subjects.Should().Equal("key", "A", "B");
        diagnostics.Where(d => d.Location == "GET /roles/inherited" && d.Code == NotWritten).Should().ContainSingle()
            .Which.Subjects.Should().Equal("key", "bearer", "A");

        fixture.Excluded.Diagnostics.Should().NotContain(d => d.Code == NotWritten);
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void RolesOnAnOAuthScheme_AreNotWrittenIntoScopes_OneWarningNamingIt(OpenApiSpecVersion version)
    {
        var (document, diagnostics) = fixture.Builds[version];
        var roles = version == OpenApiSpecVersion.OpenApi3_0 ? "[]" : """["A"]""";

        Same(Security(document, "/roles/oauth"), $$"""[{"oauth":[],"key":{{roles}}}]""").Should().BeTrue(Security(document, "/roles/oauth")!.ToJsonString());
        diagnostics.Where(d => d.Location == "GET /roles/oauth" && d.Feature == "security.roles.oauth").Should().ContainSingle()
            .Which.Subjects.Should().Equal("oauth", "A");
    }

    [Fact]
    public void RolesWithoutAnyRequirement_AreReported_NoSchemeInvented()
    {
        var (document, diagnostics) = fixture.NoRequirements;

        document["paths"]!["/roles/inherited"]!["get"]!.AsObject().ContainsKey("security").Should().BeFalse();
        diagnostics.Where(d => d.Location == "GET /roles/inherited").Should().ContainSingle()
            .Which.Code.Should().Be(ExtractionDiagnosticCodes.SecurityRolesWithoutRequirement);
    }

    [Fact]
    public void For30_QueryWithRoles_GivesTheRolesWarningAndTheMoveWarning()
    {
        const string moved = "#/paths/~1roles~1query/x-oai-additionalOperations/QUERY";
        var diagnostics = fixture.Builds[OpenApiSpecVersion.OpenApi3_0].Diagnostics;

        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.OperationMovedToAdditionalOperations && d.Location == moved);
        diagnostics.Should().ContainSingle(d => d.Code == NotWritten && d.Location == moved,
            because: "a warning about the input is never suppressed by the moved operation");
    }
}
