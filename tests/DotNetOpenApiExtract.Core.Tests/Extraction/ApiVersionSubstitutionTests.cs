using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// <c>SubstituteApiVersionInUrl</c> of Asp.Versioning: the version replaces the
/// <c>{version:apiVersion}</c> route parameter in the path, written in <c>SubstitutionFormat</c>, and
/// the parameter is gone; without the substitution the parameter is a required path parameter. The
/// expected values are those of a running Swashbuckle with the real Asp.Versioning 8.1.0
/// (<c>/api/v1/agents</c> for 1.0 in <c>VVV</c>, <c>{name: version, in: path, required: true, schema:
/// {type: string}}</c> without the substitution).
/// </summary>
public class ApiVersionSubstitutionTests
{
    private static (OpenApiDocument Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(
        string assembly, Func<OpenApiDocumentOptions, OpenApiDocumentOptions>? configure = null) =>
        VersionedDocumentHarness.BuildCollecting(onDiagnostic =>
        {
            var options = new OpenApiDocumentOptions { AssemblyPath = assembly, OnDiagnostic = onDiagnostic };
            return configure == null ? options : configure(options);
        });

    private static OpenApiOperation Get(OpenApiDocument document, string path)
    {
        document.Paths.Should().ContainKey(path, because: $"paths: {string.Join(", ", document.Paths.Keys)}");
        return ((OpenApiPathItem)document.Paths[path]).Operations![HttpMethod.Get];
    }

    private static IEnumerable<string> PathParameters(OpenApiOperation operation) =>
        (operation.Parameters ?? []).Where(p => p.In == ParameterLocation.Path).Select(p => p.Name!);

    // ── Setting read outside the entry point ────────────────────────────────

    /// <summary>
    /// The fixture also turns the substitution off between two calls of the method that turns it on — the
    /// second through a method of the project named <c>AddApiExplorer</c>, called without an options
    /// lambda — and declares a local function that turns it off but is never called: only the code that
    /// runs counts, at each of its calls, so the substitution stays on.
    /// </summary>
    [Fact]
    public void SettingInAMethodTheEntryPointCalls_SubstitutesTheVersionInTheFormatOfTheSource()
    {
        var (document, _) = Build(TestPaths.VersionedApiDll);

        // SubstitutionFormat = "VV" (a const): 1.0 is written "1.0".
        PathParameters(Get(document, "/api/v1.0/agents")).Should().BeEmpty();
        PathParameters(Get(document, "/api/v1.0/agents/{id}")).Should().Equal("id");
        document.Paths.Keys.Should().NotContain(p => p.StartsWith("/api/v{version}/agents", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatOption_WinsOverTheSource()
    {
        var (document, _) = Build(TestPaths.VersionedApiDll, o => With(o, format: "VVV"));

        PathParameters(Get(document, "/api/v1/agents")).Should().BeEmpty();
        Get(document, "/api/v1/agents/{id}");
    }

    [Fact]
    public void ValueThatIsNotAConstant_IsReportedWithItsPlace()
    {
        var (_, diagnostics) = Build(TestPaths.VersionedApiDll);

        var warning = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.ApiVersionSettingNotStatic).Subject;
        warning.SourceLocation.Should().Be("Http/ApiVersioningSetup.cs:17");
        warning.Subjects.Should().Equal("SubstituteApiVersionInUrl");
    }

    [Fact]
    public void ValueAssignedUnderACondition_IsReportedWithItsPlace()
    {
        var (_, diagnostics) = Build(TestPaths.VersionedApiDll);

        var warning = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.ApiVersionSettingConditional).Subject;
        warning.SourceLocation.Should().Be("Http/ApiVersioningSetup.cs:21");
        warning.Subjects.Should().Equal("SubstitutionFormat");
    }

    [Fact]
    public void RouteWithoutADeclaredVersion_KeepsTheParameterAndIsReported()
    {
        var (document, diagnostics) = Build(TestPaths.VersionedApiDll);

        var parameter = Get(document, "/api/v{version}/health").Parameters!.Should().ContainSingle().Subject;
        parameter.Name.Should().Be("version");
        parameter.In.Should().Be(ParameterLocation.Path);
        parameter.Required.Should().BeTrue();

        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.ApiVersionOperationUnversioned)
            .Which.Location.Should().Be("GET /api/v{version}/health");
    }

    // ── Without the substitution ────────────────────────────────────────────

    [Fact]
    public void WithoutSubstitution_TheVersionIsARequiredStringPathParameter()
    {
        var (document, diagnostics) = Build(TestPaths.SampleApiDll);

        var parameter = Get(document, "/api/v{version}/items/all").Parameters!.Should().ContainSingle().Subject;
        parameter.Name.Should().Be("version");
        parameter.In.Should().Be(ParameterLocation.Path);
        parameter.Required.Should().BeTrue();
        parameter.Schema!.Type.Should().Be(JsonSchemaType.String);
        parameter.Description.Should().Be("The requested API version: 1.0, 1.0-preview.1, 2.0.");

        PathParameters(Get(document, "/api/v{version}/items/{id}")).Should().BeEquivalentTo(["id", "version"]);
        diagnostics.Should().NotContain(d => d.Code.StartsWith("api-version.", StringComparison.Ordinal));
    }

    // ── Several versions ────────────────────────────────────────────────────

    [Fact]
    public void SubstitutionWithSeveralVersions_KeepsTheParameterAndNamesTheVersions()
    {
        var (document, diagnostics) = Build(TestPaths.SampleApiDll, o => With(o, substitute: true));

        PathParameters(Get(document, "/api/v{version}/items/all")).Should().Equal("version");
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.ApiVersionSeveralVersions)
            .Which.Subjects.Should().Equal("1.0", "1.0-preview.1", "2.0");
    }

    [Theory]
    [InlineData("2")]
    [InlineData("2.0")]
    public void ChosenVersion_IsSubstitutedAndOtherVersionsAreLeftOut(string chosen)
    {
        var (document, diagnostics) = Build(TestPaths.SampleApiDll, o => With(o, substitute: true, apiVersion: chosen));

        PathParameters(Get(document, "/api/v2/items/all")).Should().BeEmpty();
        PathParameters(Get(document, "/api/v2/items/{id}")).Should().Equal("id");
        document.Paths.Keys.Should().NotContain(p => p.Contains("{version}", StringComparison.Ordinal));

        // 2.0, a union with 2.0, version-neutral and unversioned operations stay.
        document.Paths.Keys.Should().Contain(["/api/union", "/api/status", "/api/neutralwithversion", "/api/actionneutral"]);
        // Only 1.0, 1.5 or the 2.0-rc1 prerelease: other versions.
        document.Paths.Keys.Should().NotContain(["/api/dedup", "/api/intversion", "/api/doubleversion", "/api/preview/items"]);
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.ApiVersionSeveralVersions);
    }

    [Fact]
    public void ChosenVersionWithoutSubstitution_LeavesOtherVersionsOutAndKeepsTheParameter()
    {
        var (document, _) = Build(TestPaths.SampleApiDll, o => With(o, apiVersion: "1.0"));

        Get(document, "/api/v{version}/items/all").Parameters!.Should().ContainSingle()
            .Which.Description.Should().Be("The requested API version: 1.0.");
        document.Paths.Keys.Should().NotContain("/api/v{version}/items/{id}", because: "it maps to 2.0 only");
    }

    /// <summary>
    /// <c>{version:apiVersion?}</c>: the optional marker does not hide the API version parameter, and a
    /// status with a dot (<c>preview.1</c>) is a version, written by <c>VVV</c> as Asp.Versioning 8.1.0
    /// writes it (<c>1-preview.1</c>).
    /// </summary>
    [Fact]
    public void OptionalRouteParameterAndDottedStatus_AreAnApiVersion()
    {
        var (plain, _) = Build(TestPaths.SampleApiDll);
        Get(plain, "/api/v{version}/preview").Parameters!.Should().ContainSingle()
            .Which.Name.Should().Be("version");

        var (chosen, _) = Build(TestPaths.SampleApiDll, o => With(o, substitute: true, apiVersion: "1.0-preview.1"));
        PathParameters(Get(chosen, "/api/v1-preview.1/preview")).Should().BeEmpty();
        chosen.Paths.Keys.Should().NotContain(p => p.Contains("{version}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("7.0")]
    [InlineData("1.0-pre-1")]
    public void ChosenVersionThatIsNotDeclared_IsAConfigurationError(string chosen)
    {
        var build = () => Build(TestPaths.SampleApiDll, o => With(o, apiVersion: chosen));

        build.Should().Throw<OpenApiConfigurationException>().WithMessage($"*'{chosen}'*--api-version*");
    }

    [Fact]
    public void FormatTheToolDoesNotCompute_KeepsTheParameterAndIsReported()
    {
        var (document, diagnostics) = Build(TestPaths.VersionedApiDll, o => With(o, format: "G"));

        PathParameters(Get(document, "/api/v{version}/agents")).Should().Equal("version");
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.ApiVersionSubstitutionNotComputed)
            .Which.Subjects.Should().Equal("G", "1.0");
    }

    /// <summary>
    /// The formats against the output of <c>ApiVersion.ToString(format)</c> of Asp.Versioning 8.1.0 for
    /// 2.0 and 2.0-beta (padding, literals, escapes, the status).
    /// </summary>
    [Theory]
    [InlineData("V", "2.0", "/api/v2/items/all")]
    [InlineData("VVVV", "2.0", "/api/v2.0/items/all")]
    [InlineData("PP", "2.0", "/api/v02.00/items/all")]
    [InlineData("P3", "2.0", "/api/v002/items/all")]
    [InlineData("V'_'v", "2.0", "/api/v2_0/items/all")]
    [InlineData("V\\V", "2.0", "/api/v2V/items/all")]
    public void SubstitutionFormat_IsWrittenAsAspVersioningWritesIt(string format, string chosen, string path)
    {
        var (document, _) = Build(TestPaths.SampleApiDll, o => With(o, substitute: true, apiVersion: chosen, format: format));

        Get(document, path);
    }

    private static OpenApiDocumentOptions With(
        OpenApiDocumentOptions options, bool? substitute = null, string? apiVersion = null, string? format = null) => new()
    {
        AssemblyPath                 = options.AssemblyPath,
        OnDiagnostic                 = options.OnDiagnostic,
        SubstituteApiVersionInUrl    = substitute,
        ApiVersion                   = apiVersion,
        ApiVersionSubstitutionFormat = format,
    };
}
