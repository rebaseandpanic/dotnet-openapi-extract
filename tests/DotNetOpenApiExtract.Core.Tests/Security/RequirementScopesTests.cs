using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Security;

/// <summary>ModernApi with a Program.cs whose requirements list scopes, in every version.</summary>
public sealed class RequirementScopesFixture : IDisposable
{
    private const string Program = """
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("oauth", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    ClientCredentials = new OpenApiOAuthFlow
                    {
                        TokenUrl = new Uri("https://auth.example.com/token"),
                        Scopes = new Dictionary<string, string> { ["read"] = "Read", ["write"] = "Write", ["admin"] = "Admin" },
                    },
                },
            });
            c.AddSecurityDefinition("oidc", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OpenIdConnect,
                OpenIdConnectUrl = new Uri("https://auth.example.com/.well-known/openid-configuration"),
            });
            c.AddSecurityDefinition("key", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, Name = "X-Key", In = ParameterLocation.Header });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("oauth", document)] = ["read", "write"],
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("oidc"), new List<string> { "openid" } },
                { new OpenApiSecuritySchemeReference("key"), ["ignored"] },
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "oauth", Type = ReferenceType.SecurityScheme } }, new[] { "admin" } },
            });
        });
        var app = builder.Build();
        app.MapControllers();
        app.Run();
        """;

    private readonly TempDirectory _directory = new();

    public RequirementScopesFixture()
    {
        File.WriteAllText(Path.Combine(_directory.Path, "Program.cs"), Program);
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                SourceRoot     = _directory.Path,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Documents[version] = JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;
            Diagnostics[version] = diagnostics;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];

    public void Dispose() => _directory.Dispose();
}

/// <summary>
/// The scopes listed next to an OAuth2 or OpenID Connect scheme in <c>AddSecurityRequirement</c> are
/// the values of the document's requirement; other schemes keep an empty list.
/// </summary>
public class RequirementScopesTests(RequirementScopesFixture fixture) : IClassFixture<RequirementScopesFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void RequirementScopes_AreWrittenForOAuthAndOidc_OtherSchemesKeepEmptyValues(OpenApiSpecVersion version)
    {
        var expected = JsonNode.Parse("""
            [
              { "oauth": ["read", "write"] },
              { "oidc": ["openid"], "key": [] },
              { "oauth": ["admin"] }
            ]
            """);

        JsonNode.DeepEquals(fixture.Documents[version]["security"], expected).Should().BeTrue(fixture.Documents[version]["security"]!.ToJsonString());
        fixture.Diagnostics[version].Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScopes);
    }

    [Fact]
    public void NonLiteralScopes_AreWrittenEmpty_WithAWarning()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), """
            var scopes = LoadScopes();
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("oauth", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows { Implicit = new OpenApiOAuthFlow { AuthorizationUrl = new Uri("https://a.example.com"), Scopes = new Dictionary<string, string>() } },
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("oauth"), scopes } });
            });
            """);
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.ModernApiDll,
            XmlPath      = TestPaths.ModernApiXml,
            SourceRoot   = directory.Path,
            OnDiagnostic = onDiagnostic,
        });
        var json = JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, CancellationToken.None).GetAwaiter().GetResult())!;

        JsonNode.DeepEquals(json["security"], JsonNode.Parse("""[{ "oauth": [] }]""")).Should().BeTrue();
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScopes)
            .Which.Subjects.Should().Equal("scopes");
    }

    private static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) BuildWith(string flowScopes, string requirementScopes)
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), $$"""
            var extra = LoadScopes();
            var baseScopes = LoadScopeDescriptions();
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("oauth", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows { Implicit = new OpenApiOAuthFlow { AuthorizationUrl = new Uri("https://a.example.com"), Scopes = {{flowScopes}} } },
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("oauth"), {{requirementScopes}} } });
            });
            """);
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.ModernApiDll,
            XmlPath      = TestPaths.ModernApiXml,
            SourceRoot   = directory.Path,
            OnDiagnostic = onDiagnostic,
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    private const string KnownFlowScopes = "new Dictionary<string, string>(StringComparer.Ordinal) { [\"read\"] = \"Read\" }";

    [Theory]
    [InlineData("[.. extra]")]
    [InlineData("[\"read\", .. extra]")]
    [InlineData("new List<string>(extra)")]
    public void RequirementScopesWithUnknownElements_AreWrittenEmpty_WithAWarning(string scopes)
    {
        var (document, diagnostics) = BuildWith(KnownFlowScopes, scopes);

        JsonNode.DeepEquals(document["security"], JsonNode.Parse("""[{ "oauth": [] }]""")).Should().BeTrue(document["security"]!.ToJsonString());
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScopes);
    }

    [Theory]
    [InlineData("new List<string>()")]
    [InlineData("new List<string>(4)")]
    public void EmptyOrCapacityRequirementScopes_AreAKnownEmptyList(string scopes)
    {
        var (document, diagnostics) = BuildWith(KnownFlowScopes, scopes);

        JsonNode.DeepEquals(document["security"], JsonNode.Parse("""[{ "oauth": [] }]""")).Should().BeTrue();
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScopes);
        document["components"]!["securitySchemes"]!["oauth"]!["flows"]!["implicit"]!["scopes"]!.ToJsonString().Should().Be("""{"read":"Read"}""");
    }

    [Fact]
    public void FlowScopesCopiedFromAnotherDictionary_MakeTheSchemeNotStatic()
    {
        var (document, diagnostics) = BuildWith("new Dictionary<string, string>(baseScopes)", "[]");

        document["components"]?["securitySchemes"]?.AsObject().ContainsKey("oauth").Should().NotBe(true);
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeNotStatic)
            .Which.Subjects.Should().Equal("oauth");
    }
}
