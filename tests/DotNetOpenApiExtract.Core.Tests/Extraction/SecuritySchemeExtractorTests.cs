using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="SecuritySchemeExtractor"/>.
/// </summary>
public class SecuritySchemeExtractorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Empty context → empty result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ContextUnavailable_ReturnsEmpty()
    {
        var result = SecuritySchemeExtractor.Extract(SourceAnalysisContext.Empty);

        result.Schemes.Should().BeEmpty();
        result.GlobalRequirementSchemeNames.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. AddJwtBearer without explicit name → "Bearer"
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddJwtBearerNoName_EmitsBearerScheme()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(options => { });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("Bearer");
        var scheme = result.Schemes["Bearer"];
        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("bearer");
        scheme.BearerFormat.Should().Be("JWT");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. AddJwtBearer with explicit name → uses given name
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddJwtBearerWithName_UsesGivenName()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer("MyScheme", options => { });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("MyScheme");
        result.Schemes.Should().NotContainKey("Bearer");
        var scheme = result.Schemes["MyScheme"];
        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("bearer");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. AddSecurityDefinition with object initializer → parses Http properties
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddSecurityDefinitionWithObjectInitializer_ParsesProperties()
    {
        var source = """
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("Bearer");
        var scheme = result.Schemes["Bearer"];
        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("bearer");
        scheme.BearerFormat.Should().Be("JWT");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. AddSecurityDefinition for ApiKey in header
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddSecurityDefinition_ApiKey_InHeader()
    {
        var source = """
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "X-Api-Key",
                In = ParameterLocation.Header
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("ApiKey");
        var scheme = result.Schemes["ApiKey"];
        scheme.Type.Should().Be(SecuritySchemeType.ApiKey);
        scheme.Name.Should().Be("X-Api-Key");
        scheme.In.Should().Be(Microsoft.OpenApi.ParameterLocation.Header);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Non-literal scheme name → skipped gracefully
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_MalformedInvocation_SkippedGracefully()
    {
        // Non-literal first argument (variable or config accessor)
        var source = """
            var schemeName = config["SchemeName"];
            options.AddSecurityDefinition(schemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer"
            });
            """;

        var context = BuildContext(source);
        // Must not throw; result either has no scheme or has an empty scheme dict.
        var result = SecuritySchemeExtractor.Extract(context);
        result.Schemes.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Multiple AddSecurityDefinition with different names → all emitted
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_MultipleAddSecurityDefinition_DifferentNames_AllEmitted()
    {
        var source = """
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "X-Api-Key",
                In = ParameterLocation.Header
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("Bearer");
        result.Schemes.Should().ContainKey("ApiKey");
        result.Schemes.Should().HaveCount(2,
            because: "two definitions with distinct names must both be registered");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. AddSecurityRequirement with OpenApiSecuritySchemeReference literal → global requirement
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddSecurityRequirement_LiteralSchemeName_EmittedInGlobalRequirements()
    {
        var source = """
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("Bearer"), Array.Empty<string>() }
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.GlobalRequirementSchemeNames.Should().ContainSingle()
            .Which.Should().Be("Bearer",
                because: "the string literal in OpenApiSecuritySchemeReference ctor is the scheme id");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. AddSecurityRequirement with non-literal scheme name → result is empty
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddSecurityRequirement_NonLiteralSchemeName_ReturnsEmpty()
    {
        var source = """
            var schemeName = GetSchemeName();
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference(schemeName), Array.Empty<string>() }
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.GlobalRequirementSchemeNames.Should().BeEmpty(
            because: "a non-literal scheme name in the reference ctor cannot be resolved statically");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. FQN-prefixed type name — 5.1.1 integration
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_FQN_TypeName_OpenApiSecurityScheme_Extracted()
    {
        // Production pattern: fully-qualified type name in new expression.
        var source = """
            options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                Name = "X-Api-Key",
                In = ParameterLocation.Header
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("ApiKey",
            because: "FQN type name 'Microsoft.OpenApi.OpenApiSecurityScheme' contains 'SecurityScheme' and must be recognised");
        var scheme = result.Schemes["ApiKey"];
        scheme.Type.Should().Be(SecuritySchemeType.ApiKey);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. FQN-prefixed enum values — 5.1.2 verification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_FQN_EnumValue_SecuritySchemeType_Extracted()
    {
        // Production pattern: fully-qualified enum value.
        // The rightmost identifier is still "ApiKey", which is what ParseSecuritySchemeType uses.
        var source = """
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey,
                Name = "X-Api-Key",
                In = ParameterLocation.Header
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("ApiKey");
        result.Schemes["ApiKey"].Type.Should().Be(SecuritySchemeType.ApiKey,
            because: "ParseSecuritySchemeType reads only mae.Name (rightmost identifier) so FQN prefix is irrelevant");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5.2 — Lambda-factory pattern tests
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 5.2.1 — AddSecurityRequirement with expression-body lambda (doc => new ...).
    /// Single scheme reference extracted correctly.
    /// </summary>
    [Fact]
    public void Extract_AddSecurityRequirement_LambdaFactory_ExpressionBody_ExtractsSchemeNames()
    {
        var source = """
            options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("ApiKey", doc, null), new List<string>() }
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.GlobalRequirementSchemeNames.Should().ContainSingle()
            .Which.Should().Be("ApiKey",
                because: "the string literal 'ApiKey' in the lambda-factory OpenApiSecuritySchemeReference ctor must be extracted");
    }

    /// <summary>
    /// 5.2.1 — Lambda-factory with multiple scheme references — all names extracted.
    /// </summary>
    [Fact]
    public void Extract_AddSecurityRequirement_LambdaFactory_MultipleSchemes_AllExtracted()
    {
        var source = """
            options.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("ApiKey", doc, null), new List<string>() },
                { new OpenApiSecuritySchemeReference("ClientId", doc, null), new List<string>() }
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.GlobalRequirementSchemeNames.Should().HaveCount(2,
            because: "both scheme references in the lambda-factory initializer must be extracted");
        result.GlobalRequirementSchemeNames.Should().Contain("ApiKey");
        result.GlobalRequirementSchemeNames.Should().Contain("ClientId");
    }

    /// <summary>
    /// 5.2.1 — Lambda-factory with FQN type names (as observed in real production Program.cs files).
    /// Both OpenApiSecurityRequirement and OpenApiSecuritySchemeReference are fully qualified.
    /// </summary>
    [Fact]
    public void Extract_AddSecurityRequirement_LambdaFactory_FQN_TypeNames()
    {
        var source = """
            c.AddSecurityRequirement(doc => new Microsoft.OpenApi.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.OpenApiSecuritySchemeReference("ApiKey", doc, null),
                    new List<string>()
                },
                {
                    new Microsoft.OpenApi.OpenApiSecuritySchemeReference("ClientId", doc, null),
                    new List<string>()
                }
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.GlobalRequirementSchemeNames.Should().HaveCount(2,
            because: "FQN type names must be matched via Contains('SecuritySchemeReference') substring check");
        result.GlobalRequirementSchemeNames.Should().Contain("ApiKey");
        result.GlobalRequirementSchemeNames.Should().Contain("ClientId");
    }

    /// <summary>
    /// 5.2.1 — Lambda-factory with block-body: doc => { return new OpenApiSecurityRequirement { ... }; }.
    /// </summary>
    [Fact]
    public void Extract_AddSecurityRequirement_LambdaFactory_BlockBody_ExtractsSchemeNames()
    {
        var source = """
            options.AddSecurityRequirement(doc =>
            {
                return new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("Bearer", doc, null), Array.Empty<string>() }
                };
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.GlobalRequirementSchemeNames.Should().ContainSingle()
            .Which.Should().Be("Bearer",
                because: "block-body lambda must be walked into and the scheme name extracted");
    }

    /// <summary>
    /// 5.2.1 — Regression: direct object creation (non-lambda) still works after 5.2 changes.
    /// </summary>
    [Fact]
    public void Extract_AddSecurityRequirement_DirectObjectCreation_StillWorks()
    {
        var source = """
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("Bearer"), Array.Empty<string>() }
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.GlobalRequirementSchemeNames.Should().ContainSingle()
            .Which.Should().Be("Bearer",
                because: "the original direct object-creation pattern must continue to work");
    }

    /// <summary>
    /// 5.2.2 — AddSwaggerGen with combined calls in one lambda:
    /// AddSecurityDefinition + AddSecurityRequirement are both extracted.
    /// </summary>
    [Fact]
    public void Extract_AddSwaggerGen_CombinedCalls_AllExtracted()
    {
        var source = """
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference("Bearer"), Array.Empty<string>() }
                });
            });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("Bearer",
            because: "AddSecurityDefinition inside AddSwaggerGen lambda must be found via DescendantNodes()");
        result.GlobalRequirementSchemeNames.Should().ContainSingle()
            .Which.Should().Be("Bearer",
                because: "AddSecurityRequirement inside AddSwaggerGen lambda must be found via DescendantNodes()");
    }

    /// <summary>
    /// 5.2.3 — AddJwtBearer with options lambda (no explicit name → "Bearer").
    /// Scheme is extracted correctly regardless of options lambda presence.
    /// </summary>
    [Fact]
    public void Extract_AddJwtBearer_WithOptionsLambda_ReturnsBearerScheme()
    {
        var source = """
            builder.Services.AddAuthentication()
                .AddJwtBearer("Bearer", options =>
                {
                    options.Authority = "https://auth.example.com";
                    options.Audience = "api";
                });
            """;

        var context = BuildContext(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("Bearer",
            because: "AddJwtBearer with explicit name and options lambda must produce a Bearer scheme");
        var scheme = result.Schemes["Bearer"];
        scheme.Type.Should().Be(SecuritySchemeType.Http);
        scheme.Scheme.Should().Be("bearer");
        scheme.BearerFormat.Should().Be("JWT");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Fix W1 — const string scheme name resolved via semantic model
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Regression test for Fix W1: verifies that <c>GetLiteralStringArgument</c> is called
    /// with the compilation, so in-project <c>const string</c> scheme names are resolved
    /// via the semantic model rather than silently skipped.
    /// </summary>
    [Fact]
    public void Extract_AddSecurityDefinition_ConstStringSchemeName_ResolvedViaSemanticModel()
    {
        // The source defines a const in the same compilation and references it as the
        // scheme name argument.  Without semantic-model resolution the call is skipped;
        // with it, "Bearer" is produced.
        var source = """
            public static class SchemeNames
            {
                public const string Bearer = "Bearer";
            }
            options.AddSecurityDefinition(SchemeNames.Bearer, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            """;

        // Must use a compilation with runtime references so that the semantic model can
        // resolve the const string value of SchemeNames.Bearer.
        var context = BuildContextWithReferences(source);
        var result = SecuritySchemeExtractor.Extract(context);

        result.Schemes.Should().ContainKey("Bearer",
            because: "the const string SchemeNames.Bearer = \"Bearer\" must be resolved " +
                     "via the semantic model when compilation is passed to GetLiteralStringArgument");
        result.Schemes["Bearer"].Type.Should().Be(SecuritySchemeType.Http);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> from an inline source string using
    /// a top-level statement compilation, allowing
    /// <see cref="InvocationMatcher.FindInvocations"/> to traverse the full tree.
    /// </summary>
    private static SourceAnalysisContext BuildContext(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        // Build a minimal SourceCompilationResult and use CompilationUnitSyntax
        // (top-level statements node) as the entry-point.
        var compilationResult = new SourceCompilationResult("/inline", compilation, [tree]);
        var entryPointNode = (CSharpSyntaxTree)tree;
        var root = entryPointNode.GetCompilationUnitRoot();

        return new SourceAnalysisContext(compilationResult, root);
    }

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> with full .NET runtime references,
    /// enabling semantic-model constant resolution (e.g. <c>const string</c> members).
    /// Required for tests that exercise the semantic path in
    /// <see cref="InvocationMatcher.GetLiteralStringArgument(InvocationExpressionSyntax, int, CSharpCompilation?)"/>.
    /// </summary>
    private static SourceAnalysisContext BuildContextWithReferences(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);

        // Include all DLLs from the .NET runtime directory so that string/object
        // type resolution works correctly. The semantic model needs at minimum
        // System.Private.CoreLib to resolve 'string' const values.
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>();
        foreach (var dll in System.IO.Directory.GetFiles(runtimeDir, "*.dll"))
        {
            try { references.Add(MetadataReference.CreateFromFile(dll)); }
            catch { /* skip inaccessible DLLs */ }
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult("/inline", compilation, [tree]);
        var root = ((CSharpSyntaxTree)tree).GetCompilationUnitRoot();

        return new SourceAnalysisContext(compilationResult, root);
    }
}
