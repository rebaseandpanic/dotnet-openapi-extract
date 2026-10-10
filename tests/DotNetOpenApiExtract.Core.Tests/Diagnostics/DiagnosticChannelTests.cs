using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Diagnostics;

/// <summary>
/// The public diagnostics channel: every warning Core produced before the channel existed
/// reaches the <c>OnDiagnostic</c> subscriber once, with its code, subjects and place, and
/// nothing is printed to stderr while a subscriber is attached; without one, the warnings are
/// printed as before.
/// </summary>
[Collection(ConsoleErrorCollection.Name)]
public class DiagnosticChannelTests
{
    // ── Inputs: one per existing warning category ────────────────────────────

    private const string ApiKeyScheme =
        "new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = \"X-Api-Key\" }";

    /// <summary>A case: the analysed Program.cs, extra options, and the diagnostic it must produce.</summary>
    public sealed record ChannelCase(
        string Name,
        string ProgramBody,
        string Code,
        string? Location,
        string[] Subjects,
        string? ContactUrl = null,
        string? LicenseUrl = null,
        string? TermsOfService = null)
    {
        public override string ToString() => Name;
    }

    public static TheoryData<ChannelCase> ExistingCategories =>
    [
        new ChannelCase("duplicate security scheme",
            $$"""
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
                c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
            });
            """,
            ExtractionDiagnosticCodes.SecurityDuplicateScheme, "#/components/securitySchemes/ApiKey", ["ApiKey"]),
        new ChannelCase("non-literal AddSecurityDefinition name",
            $$"""
            builder.Services.AddSwaggerGen(c => c.AddSecurityDefinition(args[0], {{ApiKeyScheme}}));
            """,
            ExtractionDiagnosticCodes.SecurityDefinitionNonLiteralName, null, []),
        new ChannelCase("non-literal AddSecurityRequirement scheme name",
            """
            builder.Services.AddSwaggerGen(c => c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference(args[0]), [] }
            }));
            """,
            ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScheme, null, ["args[0]"]),
        new ChannelCase("operation requirement on undeclared scheme",
            "",
            ExtractionDiagnosticCodes.SecurityRequirementUndeclaredScheme, "GET /api/secure/admin", ["Bearer"]),
        new ChannelCase("document requirement on undeclared scheme",
            """
            builder.Services.AddSwaggerGen(c => c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("Missing"), [] }
            }));
            """,
            ExtractionDiagnosticCodes.SecurityRequirementUndeclaredScheme, "#/security", ["Missing"]),
        new ChannelCase("invalid contact url",
            "", ExtractionDiagnosticCodes.InfoInvalidUri, "#/info/contact/url", ["not a uri"],
            ContactUrl: "not a uri"),
        new ChannelCase("invalid license url",
            "", ExtractionDiagnosticCodes.InfoInvalidUri, "#/info/license/url", ["not a uri"],
            LicenseUrl: "not a uri"),
        new ChannelCase("invalid terms of service",
            "", ExtractionDiagnosticCodes.InfoInvalidUri, "#/info/termsOfService", ["not a uri"],
            TermsOfService: "not a uri"),
        new ChannelCase("non-literal global content type",
            """
            builder.Services.AddControllers(o => o.Filters.Add(new ProducesAttribute(args[0])));
            """,
            ExtractionDiagnosticCodes.MediaTypesNonLiteralContentType, null, ["ProducesAttribute"]),
        new ChannelCase("non-literal JSON options setting",
            """
            JsonNamingPolicy? policy = null;
            builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.PropertyNamingPolicy = policy; });
            """,
            ExtractionDiagnosticCodes.JsonOptionsNonLiteralSetting, null, ["PropertyNamingPolicy"]),
        new ChannelCase("untyped JSON converter",
            """
            builder.Services.ConfigureHttpJsonOptions(o => { o.SerializerOptions.Converters.Add(new()); });
            """,
            ExtractionDiagnosticCodes.JsonOptionsUntypedConverter, null, []),
        new ChannelCase("several UsePathBase calls",
            """
            var app = builder.Build();
            app.UsePathBase("/a");
            app.UsePathBase("/b");
            app.Run();
            """,
            ExtractionDiagnosticCodes.PathBaseMultipleCalls, null, []),
        new ChannelCase("non-literal UsePathBase",
            """
            var app = builder.Build();
            app.UsePathBase(args[0]);
            app.Run();
            """,
            ExtractionDiagnosticCodes.PathBaseNonLiteral, null, []),
        new ChannelCase("non-literal header name in a call",
            """
            var app = builder.Build();
            app.Use(async (ctx, next) => { ctx.Response.Headers.Append(args[0], "v"); await next(); });
            app.Run();
            """,
            ExtractionDiagnosticCodes.ResponseHeaderNonLiteralName, null, ["Response.Headers.Append()"]),
        new ChannelCase("non-literal header name in an indexer",
            """
            var app = builder.Build();
            app.Use(async (ctx, next) => { ctx.Response.Headers[args[0]] = "v"; await next(); });
            app.Run();
            """,
            ExtractionDiagnosticCodes.ResponseHeaderNonLiteralName, null, ["Response.Headers[...]"]),
    ];

    [Theory]
    [MemberData(nameof(ExistingCategories))]
    public void Build_ExistingWarning_IsDeliveredOnceToSubscriberAndNotPrinted(ChannelCase channelCase)
    {
        using var tempDir = WriteProgram(channelCase.ProgramBody);
        var diagnostics = new List<ExtractionDiagnostic>();

        var stderr = CaptureStdErr(() => OpenApiDocumentBuilder.Build(SampleApiOptions(tempDir, channelCase, diagnostics.Add)));

        var matching = diagnostics
            .Where(d => d.Code == channelCase.Code && d.Location == channelCase.Location)
            .ToList();
        matching.Should().ContainSingle();
        matching[0].Subjects.Should().Equal(channelCase.Subjects);
        stderr.Should().BeEmpty();
    }

    // ── Without a subscriber ─────────────────────────────────────────────────

    /// <summary>Program.cs that produces several different warnings in one build.</summary>
    private const string SeveralWarningsProgram =
        $$"""
        builder.Services.AddSwaggerGen(c =>
        {
            c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
            c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
            c.AddSecurityDefinition(args[0], {{ApiKeyScheme}});
        });
        var app = builder.Build();
        app.UsePathBase("/a");
        app.UsePathBase("/b");
        app.Run();
        """;

    [Fact]
    public void Build_WithoutSubscriber_PrintsOneWarningLinePerDiagnostic()
    {
        using var tempDir = WriteProgram(SeveralWarningsProgram);
        var delivered = new List<ExtractionDiagnostic>();

        var silent = CaptureStdErr(() => OpenApiDocumentBuilder.Build(SampleApiOptions(tempDir, null, delivered.Add)));
        var printed = CaptureStdErr(() => OpenApiDocumentBuilder.Build(SampleApiOptions(tempDir, null, null)));

        silent.Should().BeEmpty();
        delivered.Count.Should().BeGreaterThan(1);
        WarningLines(printed).Should().HaveCount(delivered.Count);
    }

    [Fact]
    public async Task Cli_PrintsOneWarningLinePerDiagnostic()
    {
        using var tempDir = WriteProgram(SeveralWarningsProgram);
        var delivered = new List<ExtractionDiagnostic>();
        CaptureStdErr(() => OpenApiDocumentBuilder.Build(SampleApiOptions(tempDir, null, delivered.Add)));

        var result = await CliRunner.RunAsync(
            [
                "--assembly", TestPaths.SampleApiDll,
                "--source-root", tempDir.Path,
                "--output", Path.Combine(tempDir.Path, "openapi.json"),
            ],
            tempDir.Path,
            TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0, because: result.StdErr);
        delivered.Should().NotBeEmpty();
        WarningLines(result.StdErr).Should().HaveCount(delivered.Count);
    }

    // ── Deduplication within one build ───────────────────────────────────────

    [Fact]
    public void Build_SameWarningTwice_IsDeliveredOnce_DifferentWarnings_Each()
    {
        using var tempDir = WriteProgram(
            $$"""
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
                c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
                c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
                c.AddSecurityDefinition("Other", {{ApiKeyScheme}});
                c.AddSecurityDefinition("Other", {{ApiKeyScheme}});
            });
            """);
        var diagnostics = new List<ExtractionDiagnostic>();

        OpenApiDocumentBuilder.Build(SampleApiOptions(tempDir, null, diagnostics.Add));

        diagnostics
            .Where(d => d.Code == ExtractionDiagnosticCodes.SecurityDuplicateScheme)
            .Select(d => d.Subjects.Single())
            .Should().BeEquivalentTo(["ApiKey", "Other"]);
    }

    // ── Direct schema generation ─────────────────────────────────────────────

    /// <summary>A converter the registry of known converters does not contain.</summary>
    public sealed class UnregisteredConverter : System.Text.Json.Serialization.JsonConverter<string>
    {
        public override string Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
            => reader.GetString() ?? string.Empty;

        public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    public sealed class WithUnregisteredConverter
    {
        [System.Text.Json.Serialization.JsonConverter(typeof(UnregisteredConverter))]
        public string Value { get; set; } = string.Empty;
    }

    [Fact]
    public void SchemaGenerator_UnknownConverter_DeliveredOncePerInstance()
    {
        var first = new List<ExtractionDiagnostic>();
        var generator = new SchemaGenerator(new SchemaOptions { OnDiagnostic = first.Add });

        var stderr = CaptureStdErr(() =>
        {
            generator.GenerateSchema(typeof(WithUnregisteredConverter));
            generator.GenerateSchema(typeof(WithUnregisteredConverter));
        });

        var second = new List<ExtractionDiagnostic>();
        new SchemaGenerator(new SchemaOptions { OnDiagnostic = second.Add })
            .GenerateSchema(typeof(WithUnregisteredConverter));

        first.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SchemaUnknownJsonConverter)
            .Which.Subjects.Should().Equal(typeof(UnregisteredConverter).FullName);
        second.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SchemaUnknownJsonConverter);
        stderr.Should().BeEmpty();
    }

    // ── Public extractor overloads ───────────────────────────────────────────

    [Fact]
    public void SecuritySchemeExtractor_Overload_DeliversToCallback_OldSignature_Prints()
    {
        var context = BuildInlineContext(
            $$"""
            c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
            c.AddSecurityDefinition("ApiKey", {{ApiKeyScheme}});
            """);
        var delivered = new List<ExtractionDiagnostic>();

        var silent  = CaptureStdErr(() => SecuritySchemeExtractor.Extract(context, delivered.Add));
        var printed = CaptureStdErr(() => SecuritySchemeExtractor.Extract(context));

        delivered.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecurityDuplicateScheme);
        silent.Should().BeEmpty();
        WarningLines(printed).Should().ContainSingle();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TempDirectory WriteProgram(string body)
    {
        var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            "var builder = WebApplication.CreateBuilder(args);\n" + body + "\n");
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        return tempDir;
    }

    private static OpenApiDocumentOptions SampleApiOptions(
        TempDirectory tempDir, ChannelCase? channelCase, Action<ExtractionDiagnostic>? onDiagnostic) => new()
    {
        AssemblyPath   = TestPaths.SampleApiDll,
        XmlPath        = TestPaths.SampleApiXml,
        SourceRoot     = tempDir.Path,
        ContactUrl     = channelCase?.ContactUrl,
        LicenseName    = channelCase?.LicenseUrl != null ? "MIT" : null,
        LicenseUrl     = channelCase?.LicenseUrl,
        TermsOfService = channelCase?.TermsOfService,
        OnDiagnostic   = onDiagnostic,
    };

    private static string CaptureStdErr(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }

    private static IReadOnlyList<string> WarningLines(string stderr) =>
        stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Warning:", StringComparison.Ordinal))
            .ToList();

    private static SourceAnalysisContext BuildInlineContext(string swaggerGenBody)
    {
        var source = "builder.Services.AddSwaggerGen(c =>\n{\n" + swaggerGenBody + "\n});\n";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "InlineProgram",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        return new SourceAnalysisContext(
            new SourceCompilationResult("/inline", compilation, [tree]),
            ((CSharpSyntaxTree)tree).GetCompilationUnitRoot());
    }
}
