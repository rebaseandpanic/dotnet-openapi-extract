using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

/// <summary>
/// Swagger configured outside the entry point (an extension method, Startup, an options class) is not
/// read; the build says so with a warning instead of producing an incomplete document silently. A
/// project whose entry point configures nothing, or configures everything itself, gets no warning.
/// </summary>
public class ConfigurationOutsideEntryPointTests
{
    private static IReadOnlyList<ExtractionDiagnostic> Diagnostics(string assemblyPath, string? sourceRoot)
    {
        var (_, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = assemblyPath,
            SourceRoot     = sourceRoot,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_1,
            OnDiagnostic   = onDiagnostic,
        });
        return diagnostics;
    }

    /// <summary>
    /// The diagnostics of StrayEntryApi (it references Swashbuckle.AspNetCore.SwaggerGen) read without its
    /// PDB, with <paramref name="statements"/> as the body of its Program.cs.
    /// </summary>
    private static IReadOnlyList<ExtractionDiagnostic> DiagnosticsOfSwaggerGenProject(string statements)
    {
        using var sourceRoot = new TempDirectory();
        File.WriteAllText(Path.Combine(sourceRoot.Path, "Program.cs"), $$"""
            var builder = WebApplication.CreateBuilder(args);
            {{statements}}
            builder.Build().Run();
            """);
        return Diagnostics(OutputWithoutPdb.Of(TestPaths.StrayEntryApiDll), sourceRoot.Path);
    }

    [Fact]
    public void SwaggerGenRegisteredInAnExtensionMethodOfAnotherFile_IsReported()
    {
        var diagnostics = Diagnostics(TestPaths.SwaggerSetupApiDll, sourceRoot: null);

        var warning = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint).Subject;
        warning.SourceLocation.Should().StartWith("Program.cs:");
        warning.Subjects.Should().Equal("Swashbuckle.AspNetCore.SwaggerGen");
    }

    public static TheoryData<string> ConfigurationElsewhere => new()
    {
        "builder.Services.AddApiSwagger();",
        "builder.Services.AddSwaggerGen(); builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();",
        "builder.Services.AddSwaggerGen(); builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();",
        "builder.Services.AddSwaggerGen(SwaggerSetup.Configure);",
        "builder.Services.AddSwaggerGen(c => SwaggerSetup.Configure(c));",
        "builder.Services.AddSwaggerGen(c => { c.IncludeXmlComments(\"api.xml\"); SwaggerSetup.Configure(c); });",
    };

    [Theory]
    [MemberData(nameof(ConfigurationElsewhere))]
    public void ConfigurationOutsideTheEntryPoint_IsReported(string statements)
    {
        DiagnosticsOfSwaggerGenProject(statements)
            .Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint);
    }

    public static TheoryData<string> NothingElsewhere => new()
    {
        // configured in the entry point
        """builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new() { Title = "T" }));""",
        """builder.Services.Configure<SwaggerGenOptions>(o => o.SwaggerDoc("v1", new() { Title = "T" }));""",
        // registered without configuration
        "builder.Services.AddSwaggerGen();",
        """builder.Services.AddSwaggerGen(c => c.IncludeXmlComments("api.xml"));""",
        "builder.Services.AddSwaggerGen(); builder.Services.ConfigureOptions<ConfigureJwtBearerOptions>();",
    };

    [Theory]
    [MemberData(nameof(NothingElsewhere))]
    public void EntryPointThatConfiguresItselfOrNothing_IsNotReported(string statements)
    {
        DiagnosticsOfSwaggerGenProject(statements)
            .Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint);
    }

    [Fact]
    public void CompiledProjectConfiguringSwaggerInProgramCs_IsNotReported()
    {
        Diagnostics(TestPaths.ConfigFormsApiDll, sourceRoot: null)
            .Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint);
    }

    /// <summary>An assembly that references neither package (ModernApi uses only the annotations) is not warned.</summary>
    [Fact]
    public void AssemblyWithoutSwaggerGenOrOpenApi_IsNotReported()
    {
        var (_, diagnostics) = ConfigFormsBuild.BuildWithSources(("Program.cs", "builder.Services.AddApiSwagger();"));

        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.DocumentConfigurationNotInEntryPoint);
    }
}
