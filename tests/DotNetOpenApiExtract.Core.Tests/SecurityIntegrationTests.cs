using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.OpenApi;

using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests verifying that security schemes and per-endpoint security
/// are correctly applied to the generated OpenAPI document.
/// </summary>
public class SecurityIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 13. Inline Program.cs with AddSecurityDefinition → scheme in components
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithInlineProgram_AddSecurityDefinition_AppearsInComponents()
    {
        using var tempDir = new TempDirectory();

        // Write a minimal Program.cs that declares a security definition via
        // AddSecurityDefinition so the Roslyn extractor can find it.
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            """);

        // Also write a minimal .csproj so SourceRootResolver does not need to walk up.
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Components.Should().NotBeNull();
        document.Components!.SecuritySchemes.Should().NotBeNull();
        document.Components.SecuritySchemes!.Should().ContainKey("Bearer");

        var scheme = (OpenApiSecurityScheme)document.Components.SecuritySchemes["Bearer"];
        scheme.Type.Should().Be(SecuritySchemeType.Http);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 14. [AllowAnonymous] on action → operation has empty Security list
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AllowAnonymous_OperationHasEmptySecurity()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // GET /api/secure/public has [AllowAnonymous] → security: []
        document.Paths.Should().ContainKey("/api/secure/public");
        var pathItem = document.Paths!["/api/secure/public"] as Microsoft.OpenApi.OpenApiPathItem;
        pathItem.Should().NotBeNull();

        var operation = pathItem!.Operations?[HttpMethod.Get];
        operation.Should().NotBeNull();

        // Empty list (not null!) signals "override global security with no requirement"
        operation!.Security.Should().NotBeNull();
        operation.Security!.Should().BeEmpty(
            because: "[AllowAnonymous] emits security: [] to override any global requirement");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 15. [Authorize] on controller, no explicit scheme → Security not set on operation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AuthorizeOnController_NoExplicitScheme_OperationSecurityNotOverridden()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // GET /api/secure has [Authorize] inherited from controller (no explicit schemes)
        // → Security should be null on the operation (inherits global)
        document.Paths.Should().ContainKey("/api/secure");
        var pathItem = document.Paths!["/api/secure"] as Microsoft.OpenApi.OpenApiPathItem;
        pathItem.Should().NotBeNull();

        var operation = pathItem!.Operations?[HttpMethod.Get];
        operation.Should().NotBeNull();

        // No per-operation security override when [Authorize] has no explicit schemes.
        // The global security requirement (if present) is inherited.
        operation!.Security.Should().BeNullOrEmpty(
            because: "[Authorize] without explicit schemes does not set per-operation security");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 16. [Authorize(AuthenticationSchemes = "Bearer")] → operation security set
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AuthorizeWithSchemes_OperationHasSchemeRequirement()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // GET /api/secure/admin has [Authorize(Policy = "Admin", AuthenticationSchemes = "Bearer")]
        document.Paths.Should().ContainKey("/api/secure/admin");
        var pathItem = document.Paths!["/api/secure/admin"] as Microsoft.OpenApi.OpenApiPathItem;
        pathItem.Should().NotBeNull();

        var operation = pathItem!.Operations?[HttpMethod.Get];
        operation.Should().NotBeNull();

        operation!.Security.Should().NotBeNull()
            .And.NotBeEmpty(because: "action has [Authorize(AuthenticationSchemes = \"Bearer\")]");

        var requirement = operation.Security![0];
        var schemeKey = requirement.Keys.FirstOrDefault();
        schemeKey.Should().NotBeNull();
        schemeKey!.Reference?.Id.Should().Be("Bearer");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 17. Lambda-factory AddSecurityRequirement → document.Security populated
    //     with scheme names in components AND in document.security
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AddSecurityRequirement_LambdaFactory_GlobalSecurityPopulated()
    {
        using var tempDir = new TempDirectory();

        // Exact VpnCoreApi-style pattern: FQN types, lambda-factory, two schemes.
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.OpenApiSecurityScheme
                {
                    Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
                    In = Microsoft.OpenApi.ParameterLocation.Header,
                    Name = "X-Api-Key"
                });
                c.AddSecurityRequirement(doc => new Microsoft.OpenApi.OpenApiSecurityRequirement
                {
                    {
                        new Microsoft.OpenApi.OpenApiSecuritySchemeReference("ApiKey", doc, null),
                        new List<string>()
                    }
                });
            });
            """);

        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // The scheme must appear in components.
        document.Components.Should().NotBeNull();
        document.Components!.SecuritySchemes.Should().ContainKey("ApiKey");

        // The document.Security must contain a requirement with "ApiKey".
        document.Security.Should().NotBeNullOrEmpty(
            because: "AddSecurityRequirement lambda-factory must populate document.security");

        var globalReq = document.Security![0];
        globalReq.Should().NotBeEmpty(
            because: "the global requirement must contain scheme references");

        var schemeKey = globalReq.Keys.FirstOrDefault();
        schemeKey.Should().NotBeNull();
        schemeKey!.Reference?.Id.Should().Be("ApiKey",
            because: "the scheme name 'ApiKey' must appear as the key in the security requirement");
    }
}
