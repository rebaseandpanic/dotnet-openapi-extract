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
    // 16. Requirement on a scheme that is not declared in components/securitySchemes
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/secure/admin carries <c>[Authorize(AuthenticationSchemes = "Bearer")]</c>
    /// but no "Bearer" scheme is declared. OpenAPI requires every requirement name to be a
    /// declared scheme, and <c>security: [{}]</c> / <c>security: []</c> would both assert
    /// anonymous access — so the operation must carry no <c>security</c> key at all and
    /// fall back to the document-level requirement (here: none).
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public async Task Serialize_OperationRequirementOnUndeclaredScheme_NoGlobal_SecurityKeyAbsent(
        OpenApiSpecVersion version)
    {
        var root = await BuildAndSerializeAsync(
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Build().Run();
            """,
            version);

        var operation = root["paths"]?["/api/secure/admin"]?["get"]?.AsObject();
        operation.Should().NotBeNull();
        operation!.ContainsKey("security").Should().BeFalse();
        root.AsObject().ContainsKey("security").Should().BeFalse();
    }

    /// <summary>
    /// Same undeclared "Bearer" operation requirement, with a declared document-level
    /// requirement: the operation must not override it, so it inherits <c>ApiKey</c>.
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public async Task Serialize_OperationRequirementOnUndeclaredScheme_InheritsGlobalRequirement(
        OpenApiSpecVersion version)
    {
        var root = await BuildAndSerializeAsync(
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("ApiKey"), [] }
                });
            });
            builder.Build().Run();
            """,
            version);

        var operation = root["paths"]?["/api/secure/admin"]?["get"]?.AsObject();
        operation.Should().NotBeNull();
        operation!.ContainsKey("security").Should().BeFalse();
        root["security"].Should().NotBeNull();
        root["security"]!.ToJsonString().Should().Be("""[{"ApiKey":[]}]""");
    }

    /// <summary>
    /// A requirement that names one declared and one undeclared scheme keeps the declared
    /// one only (an undeclared name cannot be written into a valid spec).
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public async Task Serialize_MixedRequirement_OnlyDeclaredSchemeWritten(OpenApiSpecVersion version)
    {
        var root = await BuildAndSerializeAsync(
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("ApiKey"), [] },
                    { new OpenApiSecuritySchemeReference("Undeclared"), [] }
                });
            });
            builder.Build().Run();
            """,
            version);

        root["security"].Should().NotBeNull();
        root["security"]!.ToJsonString().Should().Be("""[{"ApiKey":[]}]""");
    }

    /// <summary>
    /// GET /api/secure/public carries <c>[AllowAnonymous]</c>. With a document-level
    /// requirement in place it must be written with an explicit <c>security: []</c> — the
    /// OpenAPI way to remove the top-level requirement for one operation. A missing key
    /// would make it inherit the requirement, so the two are told apart.
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public async Task Serialize_AllowAnonymousWithGlobalRequirement_EmptySecurityArrayWritten(
        OpenApiSpecVersion version)
    {
        var root = await BuildAndSerializeAsync(ApiKeyDefinitionAnd(
            """
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("ApiKey"), [] }
            });
            """), version);

        root["security"].Should().NotBeNull();
        var operation = root["paths"]?["/api/secure/public"]?["get"]?.AsObject();
        operation.Should().NotBeNull();
        operation!.ContainsKey("security").Should().BeTrue();
        operation["security"]!.ToJsonString().Should().Be("[]");
    }

    /// <summary>
    /// Keys inside one Security Requirement Object are AND; separate array entries are OR.
    /// Each <c>AddSecurityRequirement</c> call is its own alternative (OR); several names in
    /// one call must all be satisfied together (AND).
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, true)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, true)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, true)]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, false)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, false)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, false)]
    public async Task Serialize_GlobalRequirements_EachCallIsAlternativeNamesInCallAreCombined(
        OpenApiSpecVersion version, bool separateCalls)
    {
        var requirements = separateCalls
            ? """
              c.AddSecurityRequirement(new OpenApiSecurityRequirement
              {
                  { new OpenApiSecuritySchemeReference("ApiKey"), [] }
              });
              c.AddSecurityRequirement(new OpenApiSecurityRequirement
              {
                  { new OpenApiSecuritySchemeReference("Bearer"), [] }
              });
              """
            : """
              c.AddSecurityRequirement(new OpenApiSecurityRequirement
              {
                  { new OpenApiSecuritySchemeReference("ApiKey"), [] },
                  { new OpenApiSecuritySchemeReference("Bearer"), [] }
              });
              """;
        var expected = separateCalls
            ? """[{"ApiKey":[]},{"Bearer":[]}]"""
            : """[{"ApiKey":[],"Bearer":[]}]""";

        var root = await BuildAndSerializeAsync(ApiKeyDefinitionAnd(requirements), version);

        root["security"].Should().NotBeNull();
        System.Text.Json.Nodes.JsonNode.DeepEquals(
                root["security"], System.Text.Json.Nodes.JsonNode.Parse(expected))
            .Should().BeTrue($"security must be {expected}, was {root["security"]!.ToJsonString()}");
    }

    /// <summary>
    /// Program.cs declaring <c>ApiKey</c> (AddSecurityDefinition) and <c>Bearer</c>
    /// (AddJwtBearer), followed by <paramref name="swaggerGenStatements"/> inside
    /// <c>AddSwaggerGen(c =&gt; { ... })</c>.
    /// </summary>
    private static string ApiKeyDefinitionAnd(string swaggerGenStatements) =>
        "var builder = WebApplication.CreateBuilder(args);\n" +
        "builder.Services.AddAuthentication().AddJwtBearer(o => { });\n" +
        "builder.Services.AddSwaggerGen(c =>\n{\n" +
        "    c.AddSecurityDefinition(\"ApiKey\", new OpenApiSecurityScheme\n" +
        "    {\n        Type = SecuritySchemeType.ApiKey,\n        In = ParameterLocation.Header,\n        Name = \"X-Api-Key\"\n    });\n" +
        swaggerGenStatements + "\n});\nbuilder.Build().Run();\n";

    // ──────────────────────────────────────────────────────────────────────────
    // 17. Lambda-factory AddSecurityRequirement → document.Security populated
    //     with scheme names in components AND in document.security
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AddSecurityRequirement_LambdaFactory_GlobalSecurityPopulated()
    {
        using var tempDir = new TempDirectory();

        // Real-world production pattern: FQN types, lambda-factory, two schemes.
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

    // ──────────────────────────────────────────────────────────────────────────
    // 18. Security requirements survive serialization (document- and operation-level)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The written spec — not just the in-memory model — must carry each requirement as
    /// <c>{"&lt;scheme name&gt;": []}</c> when the scheme is declared in
    /// <c>components/securitySchemes</c> (OpenAPI: "Each name MUST correspond to a security
    /// scheme which is declared in the Security Schemes under the Components Object").
    /// An empty <c>{}</c> instead would mean "no authentication required".
    /// </summary>
    /// <param name="version">Spec version passed to the serializer, as the CLI does.</param>
    /// <param name="operationPath">
    /// Path of the GET operation whose <c>security</c> is checked, or <see langword="null"/>
    /// for the document-level <c>security</c>.
    /// </param>
    /// <param name="expectedScheme">The single scheme name the requirement must contain.</param>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, null, "ApiKey")]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, null, "ApiKey")]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, null, "ApiKey")]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, "/api/secure/admin", "Bearer")]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, "/api/secure/admin", "Bearer")]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, "/api/secure/admin", "Bearer")]
    public async Task Serialize_DeclaredSchemeRequirement_WrittenWithSchemeName(
        OpenApiSpecVersion version, string? operationPath, string expectedScheme)
    {
        using var tempDir = new TempDirectory();

        // "Bearer" is declared by AddJwtBearer and required by
        // [Authorize(AuthenticationSchemes = "Bearer")] on GET /api/secure/admin;
        // "ApiKey" is declared by AddSecurityDefinition and required globally.
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(o => { });
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("ApiKey"), [] }
                });
            });
            builder.Build().Run();
            """);
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        });

        var json = await document.SerializeAsJsonAsync(version, TestContext.Current.CancellationToken);
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;

        var owner = operationPath == null ? root : root["paths"]?[operationPath]?["get"];
        owner.Should().NotBeNull();

        var security = owner!["security"];
        security.Should().NotBeNull();
        security!.ToJsonString().Should().Be($$"""[{"{{expectedScheme}}":[]}]""");
    }

    /// <summary>
    /// Builds SampleApi with <paramref name="programSource"/> as the analysed Program.cs and
    /// returns the document serialized the way the CLI writes it.
    /// </summary>
    private static async Task<System.Text.Json.Nodes.JsonNode> BuildAndSerializeAsync(
        string programSource, OpenApiSpecVersion version)
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "Program.cs"), programSource);
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        });

        var json = await document.SerializeAsJsonAsync(version, TestContext.Current.CancellationToken);
        return System.Text.Json.Nodes.JsonNode.Parse(json)!;
    }
}
