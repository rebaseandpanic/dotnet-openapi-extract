using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Security;

/// <summary>ModernApi with a Program.cs declaring OAuth2 and OpenID Connect schemes, in every version.</summary>
public sealed class OAuthOidcSchemeFixture : IDisposable
{
    public const string Program = """
        var builder = WebApplication.CreateBuilder(args);
        var dynamicFlows = LoadFlows();
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("Implicit", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    Implicit = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri("https://auth.example.com/authorize"),
                        RefreshUrl = new Uri("https://auth.example.com/refresh"),
                        Scopes = new Dictionary<string, string> { ["read"] = "Read access", { "write", "Write access" } },
                    },
                },
            });
            c.AddSecurityDefinition("Password", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    Password = new OpenApiOAuthFlow { TokenUrl = new Uri("https://auth.example.com/token"), Scopes = new Dictionary<string, string>() },
                },
            });
            c.AddSecurityDefinition("Client", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    ClientCredentials = new OpenApiOAuthFlow
                    {
                        TokenUrl = new Uri("https://auth.example.com/token"),
                        Scopes = new Dictionary<string, string> { ["admin"] = "Administration" },
                    },
                },
            });
            c.AddSecurityDefinition("Code", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri("https://auth.example.com/authorize"),
                        TokenUrl = new Uri("https://auth.example.com/token"),
                        Scopes = new Dictionary<string, string> { ["read"] = "Read access" },
                    },
                },
            });
            c.AddSecurityDefinition("Oidc", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OpenIdConnect,
                OpenIdConnectUrl = new Uri("https://auth.example.com/.well-known/openid-configuration"),
            });
            c.AddSecurityDefinition("Device", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                OAuth2MetadataUrl = new Uri("https://auth.example.com/.well-known/oauth-authorization-server"),
                Deprecated = true,
                Flows = new OpenApiOAuthFlows
                {
                    DeviceAuthorization = new OpenApiOAuthFlow
                    {
                        DeviceAuthorizationUrl = new Uri("https://auth.example.com/device"),
                        TokenUrl = new Uri("https://auth.example.com/token"),
                        Scopes = new Dictionary<string, string> { ["read"] = "Read access" },
                    },
                },
            });
            c.AddSecurityDefinition("Dynamic", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = dynamicFlows,
            });
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Dynamic", document)] = [] });
        });
        var app = builder.Build();
        app.MapControllers();
        app.Run();
        """;

    private readonly TempDirectory _directory = new();

    public OAuthOidcSchemeFixture()
    {
        File.WriteAllText(Path.Combine(_directory.Path, "Program.cs"), Program);
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            Documents[version] = Build(version, exclude: null, out var diagnostics);
            Diagnostics[version] = diagnostics;
        }

        Build(OpenApiSpecVersion.OpenApi3_0, exclude: ["/"], out var excluded);
        ExcludedDiagnostics = excluded;
    }

    private JsonNode Build(OpenApiSpecVersion version, IReadOnlyList<string>? exclude, out IReadOnlyList<ExtractionDiagnostic> diagnostics)
    {
        var (document, collected) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            SourceRoot          = _directory.Path,
            OpenApiVersion      = version,
            OnDiagnostic        = onDiagnostic,
            ExcludePathPrefixes = exclude,
        });
        diagnostics = collected;
        return JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];

    public IReadOnlyList<ExtractionDiagnostic> ExcludedDiagnostics { get; }

    public void Dispose() => _directory.Dispose();
}

/// <summary>
/// OAuth2 and OpenID Connect declarations of Program.cs: flows with every URL and scope, the OpenID
/// Connect URL, the OpenAPI 3.2 metadata by version; a literal declaration without its required data
/// is an extraction error, one that cannot be resolved statically is omitted with one warning.
/// </summary>
public class OAuthOidcSchemeTests(OAuthOidcSchemeFixture fixture) : IClassFixture<OAuthOidcSchemeFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Scheme(OpenApiSpecVersion version, string name) =>
        fixture.Documents[version]["components"]!["securitySchemes"]![name]!.AsObject();

    public static TheoryData<OpenApiSpecVersion, string, string, string> Flows()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string, string>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            data.Add(version, "Implicit", "implicit",
                """{"authorizationUrl":"https://auth.example.com/authorize","refreshUrl":"https://auth.example.com/refresh","scopes":{"read":"Read access","write":"Write access"}}""");
            data.Add(version, "Password", "password", """{"tokenUrl":"https://auth.example.com/token","scopes":{}}""");
            data.Add(version, "Client", "clientCredentials", """{"tokenUrl":"https://auth.example.com/token","scopes":{"admin":"Administration"}}""");
            data.Add(version, "Code", "authorizationCode",
                """{"authorizationUrl":"https://auth.example.com/authorize","tokenUrl":"https://auth.example.com/token","scopes":{"read":"Read access"}}""");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Flows))]
    public void OAuth2Flow_IsWrittenWithEveryUrlAndScope(OpenApiSpecVersion version, string name, string flow, string expected)
    {
        var scheme = Scheme(version, name);

        scheme["type"]!.GetValue<string>().Should().Be("oauth2");
        JsonNode.DeepEquals(scheme["flows"]![flow], JsonNode.Parse(expected)).Should().BeTrue(scheme.ToJsonString());
        scheme["flows"]!.AsObject().Select(f => f.Key).Should().Equal(flow);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void OpenIdConnect_IsWrittenWithItsUrl(OpenApiSpecVersion version)
    {
        var scheme = Scheme(version, "Oidc");

        scheme["type"]!.GetValue<string>().Should().Be("openIdConnect");
        scheme["openIdConnectUrl"]!.GetValue<string>().Should().Be("https://auth.example.com/.well-known/openid-configuration");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void NonStaticDeclaration_IsOmitted_WithOneWarningAtTheSchemesPlace(OpenApiSpecVersion version)
    {
        fixture.Documents[version]["components"]!["securitySchemes"]!.AsObject().ContainsKey("Dynamic").Should().BeFalse();
        fixture.Documents[version].AsObject().ContainsKey("security").Should().BeFalse(because: "the only requirement named the omitted scheme");

        var warning = fixture.Diagnostics[version].Where(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeNotStatic).Should().ContainSingle().Which;
        warning.Location.Should().Be("#/components/securitySchemes/Dynamic");
        warning.Subjects.Should().Equal("Dynamic");
    }

    [Fact]
    public void NonStaticDeclaration_Warning_IsKept_WhenEveryPathIsExcluded() =>
        fixture.ExcludedDiagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeNotStatic)
            .Should().ContainSingle().Which.Location.Should().Be("#/components/securitySchemes/Dynamic");

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SecurityMetadata_Is32Native_AndMovedWithOneWarningEachBefore(OpenApiSpecVersion version)
    {
        var scheme = Scheme(version, "Device");
        var flows = scheme["flows"]!.AsObject();
        var expectedFlow = JsonNode.Parse(
            """{"deviceAuthorizationUrl":"https://auth.example.com/device","tokenUrl":"https://auth.example.com/token","scopes":{"read":"Read access"}}""");
        var warnings = fixture.Diagnostics[version].Where(d => d.Subjects.Contains("Device")).ToList();

        if (version == OpenApiSpecVersion.OpenApi3_2)
        {
            JsonNode.DeepEquals(flows["deviceAuthorization"], expectedFlow).Should().BeTrue(flows.ToJsonString());
            scheme["oauth2MetadataUrl"]!.GetValue<string>().Should().Be("https://auth.example.com/.well-known/oauth-authorization-server");
            scheme["deprecated"]!.GetValue<bool>().Should().BeTrue();
            warnings.Should().BeEmpty();
            return;
        }

        // The library writes the flow and its URL under extension names; the flow's warning covers both.
        var movedFlow = JsonNode.Parse(
            """{"x-oai-deviceAuthorizationUrl":"https://auth.example.com/device","tokenUrl":"https://auth.example.com/token","scopes":{"read":"Read access"}}""");
        JsonNode.DeepEquals(flows["x-oai-deviceAuthorization"], movedFlow).Should().BeTrue(flows.ToJsonString());
        scheme["x-oai-oauth2-metadata-url"]!.GetValue<string>().Should().Be("https://auth.example.com/.well-known/oauth-authorization-server");
        scheme["x-oai-deprecated"]!.GetValue<bool>().Should().BeTrue();
        scheme.ContainsKey("oauth2MetadataUrl").Should().BeFalse();
        scheme.ContainsKey("deprecated").Should().BeFalse();

        const string pointer = "#/components/securitySchemes/Device";
        warnings.Select(w => (w.Code, w.Location)).Should().BeEquivalentTo(new[]
        {
            (ExtractionDiagnosticCodes.SecurityDeviceAuthorizationMovedToExtension, pointer + "/flows/x-oai-deviceAuthorization"),
            (ExtractionDiagnosticCodes.SecurityOAuth2MetadataUrlMovedToExtension, pointer + "/x-oai-oauth2-metadata-url"),
            (ExtractionDiagnosticCodes.SecurityDeprecatedMovedToExtension, pointer + "/x-oai-deprecated"),
        }, because: "one warning for the flow covers its URLs and scopes");
        warnings.Should().OnlyContain(w => w.Action == DiagnosticAction.MovedToExtension && w.RequiredVersion == OpenApiSpecVersion.OpenApi3_2);
    }

    // ── Literal declarations without their required data ───────────────────────

    [Theory]
    [InlineData("Type = SecuritySchemeType.OAuth2", "Flows")]
    [InlineData("Type = SecuritySchemeType.OAuth2, Flows = new OpenApiOAuthFlows()", "Flows")]
    [InlineData("Type = SecuritySchemeType.OpenIdConnect, Description = \"no url\"", "OpenIdConnectUrl")]
    [InlineData("Type = SecuritySchemeType.OAuth2, Flows = null", "Flows")]
    [InlineData("Type = SecuritySchemeType.OpenIdConnect, OpenIdConnectUrl = null", "OpenIdConnectUrl")]
    public void LiteralDeclaration_WithoutRequiredData_IsAnExtractionError(string initializer, string member)
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"),
            $$"""
            builder.Services.AddSwaggerGen(c => c.AddSecurityDefinition("Broken", new OpenApiSecurityScheme { {{initializer}} }));
            """);

        var build = () => OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.ModernApiDll,
            XmlPath      = TestPaths.ModernApiXml,
            SourceRoot   = directory.Path,
        });

        var error = build.Should().Throw<OpenApiExtractionException>().Which;
        error.TypeName.Should().Be("Microsoft.OpenApi.OpenApiSecurityScheme");
        error.MemberName.Should().Be(member);
        error.Message.Should().Contain("Broken");
    }

    [Theory]
    [InlineData("Implicit = new OpenApiOAuthFlow { Scopes = new Dictionary<string, string>() }", "AuthorizationUrl")]
    [InlineData("Implicit = new OpenApiOAuthFlow()", "AuthorizationUrl")]
    [InlineData("Password = new OpenApiOAuthFlow { AuthorizationUrl = new Uri(\"https://a.example.com\") }", "TokenUrl")]
    [InlineData("ClientCredentials = new OpenApiOAuthFlow()", "TokenUrl")]
    [InlineData("AuthorizationCode = new OpenApiOAuthFlow { AuthorizationUrl = new Uri(\"https://a.example.com\") }", "TokenUrl")]
    [InlineData("AuthorizationCode = new OpenApiOAuthFlow { TokenUrl = new Uri(\"https://a.example.com/token\") }", "AuthorizationUrl")]
    [InlineData("DeviceAuthorization = new OpenApiOAuthFlow { TokenUrl = new Uri(\"https://a.example.com/token\") }", "DeviceAuthorizationUrl")]
    [InlineData("DeviceAuthorization = new OpenApiOAuthFlow { DeviceAuthorizationUrl = new Uri(\"https://a.example.com/device\") }", "TokenUrl")]
    public void LiteralFlow_WithoutARequiredUrl_IsAnExtractionError(string flow, string member)
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"),
            $$"""
            builder.Services.AddSwaggerGen(c => c.AddSecurityDefinition("Broken", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows { {{flow}} },
            }));
            """);

        var build = () => OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.ModernApiDll,
            XmlPath      = TestPaths.ModernApiXml,
            SourceRoot   = directory.Path,
        });

        var error = build.Should().Throw<OpenApiExtractionException>().Which;
        error.TypeName.Should().Be("Microsoft.OpenApi.OpenApiOAuthFlow");
        error.MemberName.Should().Be(member);
        error.Message.Should().Contain("Broken");
    }

    [Fact]
    public void NullOptionalValues_AreKnownAbsences_TheSchemeIsKept()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), """
            builder.Services.AddSwaggerGen(c => c.AddSecurityDefinition("Nulls", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                OAuth2MetadataUrl = null,
                Flows = new OpenApiOAuthFlows
                {
                    Implicit = null,
                    Password = new OpenApiOAuthFlow { TokenUrl = new Uri("https://a.example.com/token"), RefreshUrl = null, Scopes = null },
                },
            }));
            """);
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.ModernApiDll,
            XmlPath      = TestPaths.ModernApiXml,
            SourceRoot   = directory.Path,
            OnDiagnostic = onDiagnostic,
        });
        var json = JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, CancellationToken.None).GetAwaiter().GetResult())!;

        json["components"]!["securitySchemes"]!["Nulls"]!["flows"]!.ToJsonString()
            .Should().Be("""{"password":{"tokenUrl":"https://a.example.com/token","scopes":{}}}""");
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeNotStatic);
    }

    [Fact]
    public void LiteralThatIsNotAUri_OmitsTheScheme_WithItsOwnWarning()
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), """
            const string Token = "https://auth.example.com/to ken";
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Oidc", new OpenApiSecurityScheme { Type = SecuritySchemeType.OpenIdConnect, OpenIdConnectUrl = new Uri("not a uri") });
                c.AddSecurityDefinition("Client", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows { ClientCredentials = new OpenApiOAuthFlow { TokenUrl = new Uri(Token) } },
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("Oidc"), [] } });
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

        json["components"]?["securitySchemes"]?.AsObject().Should().BeNullOrEmpty();
        json.AsObject().ContainsKey("security").Should().BeFalse();
        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeInvalidUri)
            .Select(d => (d.Location, string.Join("|", d.Subjects)))
            .Should().BeEquivalentTo(new[]
            {
                ("#/components/securitySchemes/Oidc", "Oidc|not a uri"),
                ("#/components/securitySchemes/Client", "Client|https://auth.example.com/to ken"),
            });
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeNotStatic
                                             || (d.Code == ExtractionDiagnosticCodes.SecurityRequirementUndeclaredScheme && d.Subjects.Contains("Oidc")));
    }
}
