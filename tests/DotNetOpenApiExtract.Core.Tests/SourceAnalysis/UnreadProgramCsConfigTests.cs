using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

/// <summary>
/// Configuration the extractor recognizes but cannot compute without running the application is
/// reported with a code, the place in the source and, where one exists, the CLI flag that sets the
/// value — never lost silently.
/// </summary>
public class UnreadProgramCsConfigTests
{
    /// <summary>A Program.cs whose fourth line is <paramref name="statement"/>, inside <c>AddSwaggerGen</c>.</summary>
    private static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(string statement) =>
        ConfigFormsBuild.BuildWithSources(("Program.cs", $$"""
            var settings = LoadSettings();
            builder.Services.AddSwaggerGen(c =>
            {
            {{statement}}
            });
            """));

    private const string Line = "Program.cs:4";

    public static TheoryData<string, string, string?> UnreadInfoValues => new()
    {
        // statement on line 4 → the place named first → the flag that sets the value
        { """c.SwaggerDoc("v1", new() { Title = "T", Summary = settings.Summary });""", "OpenApiInfo.Summary", "--summary" },
        { """c.SwaggerDoc("v1", new() { Title = "T", License = new() { Name = settings.License } });""", "OpenApiLicense.Name", "--license-name" },
        { """c.SwaggerDoc("v1", new() { Title = "T", License = new() { Name = "MIT", Url = new Uri(settings.Url) } });""", "OpenApiLicense.Url", "--license-url" },
        { """c.SwaggerDoc("v1", new() { Title = "T", License = new() { Name = "MIT", Identifier = settings.Spdx } });""", "OpenApiLicense.Identifier", "--license-identifier" },
        { """c.SwaggerDoc("v1", new() { Title = "T", ExternalDocs = new() { Url = new Uri(settings.Docs) } });""", "OpenApiExternalDocs.Url", null },
        { """c.SwaggerDoc("v1", new() { Title = "T", License = settings.LicenseObject });""", "OpenApiInfo.License", "--license-name" },
        { """c.SwaggerDoc("v1", settings.Info);""", "SwaggerDoc info", "--summary" },
    };

    [Theory]
    [MemberData(nameof(UnreadInfoValues))]
    public void UnreadInfoValue_IsReported_WithItsPlace_AndFlag(string statement, string place, string? flag)
    {
        var (_, diagnostics) = Build(statement);

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.DocumentMetadataNotStatic).Subject;
        diagnostic.SourceLocation.Should().Be(Line);
        diagnostic.Subjects[0].Should().Be(place);
        if (flag == null)
            diagnostic.Subjects.Should().NotContain(s => s.StartsWith("--", StringComparison.Ordinal));
        else
            diagnostic.Subjects.Should().Contain(flag);
    }

    // ── Security schemes ─────────────────────────────────────────────────────

    public static TheoryData<string> SchemesWithAnUnreadIdentity => new()
    {
        """c.AddSecurityDefinition("Key", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = settings.Header });""",
        """c.AddSecurityDefinition("Key", new OpenApiSecurityScheme { Type = settings.Type, In = ParameterLocation.Header, Name = "X-Key" });""",
        """c.AddSecurityDefinition("Key", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = settings.Location, Name = "X-Key" });""",
        """c.AddSecurityDefinition("Key", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = settings.Scheme });""",
    };

    /// <summary>A scheme whose type, location, name or HTTP scheme is unknown is not the one served: omitted, with a warning.</summary>
    [Theory]
    [MemberData(nameof(SchemesWithAnUnreadIdentity))]
    public void SchemeWithAnUnreadIdentity_IsOmitted_WithItsPlace(string statement)
    {
        var (document, diagnostics) = Build(statement);

        document["components"]?["securitySchemes"]?["Key"].Should().BeNull();
        var diagnostic = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeNotStatic).Subject;
        diagnostic.SourceLocation.Should().Be(Line);
        diagnostic.Subjects.Should().Equal("Key");
    }

    public static TheoryData<string, string, string> SchemesWithAnUnreadDescriptiveField => new()
    {
        { """c.AddSecurityDefinition("Key", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Key", Description = settings.Text });""",
          "Description", """{"type":"apiKey","name":"X-Key","in":"header"}""" },
        { """c.AddSecurityDefinition("Key", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = settings.Format });""",
          "BearerFormat", """{"type":"http","scheme":"bearer"}""" },
    };

    [Theory]
    [MemberData(nameof(SchemesWithAnUnreadDescriptiveField))]
    public void SchemeWithAnUnreadDescriptiveField_IsWrittenWithoutIt_WithAWarning(string statement, string field, string expected)
    {
        var (document, diagnostics) = Build(statement);

        var scheme = document["components"]?["securitySchemes"]?["Key"];
        scheme.Should().NotBeNull();
        JsonNode.DeepEquals(scheme, JsonNode.Parse(expected)).Should().BeTrue(scheme!.ToJsonString());
        var diagnostic = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecuritySchemeFieldNotStatic).Subject;
        diagnostic.SourceLocation.Should().Be(Line);
        diagnostic.Subjects.Take(2).Should().Equal("Key", field);
    }

    [Fact]
    public void DefinitionWithAnUnreadName_IsReported_WithItsPlace()
    {
        var (_, diagnostics) = Build(
            """c.AddSecurityDefinition(settings.Name, new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Key" });""");

        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecurityDefinitionNonLiteralName)
            .Which.SourceLocation.Should().Be(Line);
    }

    // ── Security requirements ────────────────────────────────────────────────

    private const string KeyDefinition =
        """c.AddSecurityDefinition("Key", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-Key" });""";

    public static TheoryData<string, string> UnreadRequirements => new()
    {
        { "c.AddSecurityRequirement(settings.Requirement);", "settings.Requirement" },
        { "c.AddSecurityRequirement(document => settings.Requirement);", "settings.Requirement" },
        { "c.AddSecurityRequirement(document => { return settings.Requirement; });", "settings.Requirement" },
        { "c.AddSecurityRequirement(document => BuildRequirement(document));", "BuildRequirement(document)" },
    };

    [Theory]
    [MemberData(nameof(UnreadRequirements))]
    public void RequirementThatIsNotACreation_IsReported_WithItsPlace(string statement, string expression)
    {
        var (document, diagnostics) = Build(statement + " " + KeyDefinition);

        document["security"].Should().BeNull();
        var diagnostic = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementNotStatic).Subject;
        diagnostic.SourceLocation.Should().Be(Line);
        diagnostic.Subjects.Should().Equal(expression);
    }

    [Fact]
    public void RequirementWithAnUnreadSchemeName_IsReported_WithItsPlace()
    {
        var (_, diagnostics) = Build(
            "c.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(settings.Scheme, document)] = [] });");

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScheme).Subject;
        diagnostic.SourceLocation.Should().Be(Line);
        diagnostic.Subjects.Should().Equal("settings.Scheme");
    }

    // ── Filters ──────────────────────────────────────────────────────────────

    public static TheoryData<string, string, string> FilterRegistrations => new()
    {
        { "c.OperationFilter<SecurityRequirementsFilter>();", "OperationFilter", "SecurityRequirementsFilter" },
        { "c.DocumentFilter<SecurityDocumentFilter>();", "DocumentFilter", "SecurityDocumentFilter" },
        { "c.AddOperationFilterInstance(new SecurityRequirementsFilter());", "AddOperationFilterInstance", "new SecurityRequirementsFilter()" },
        { "c.AddDocumentFilterInstance(filter);", "AddDocumentFilterInstance", "filter" },
        { "builder.Services.AddOpenApi(o => o.AddDocumentTransformer<SecurityTransformer>());", "AddDocumentTransformer", "SecurityTransformer" },
        { "builder.Services.AddOpenApi(o => o.AddOperationTransformer<SecurityTransformer>());", "AddOperationTransformer", "SecurityTransformer" },
    };

    /// <summary>A filter registered without any readable requirement may set the requirements at run time: a warning per registration.</summary>
    [Theory]
    [MemberData(nameof(FilterRegistrations))]
    public void FilterWithoutReadRequirements_IsReported(string statement, string method, string filter)
    {
        var (_, diagnostics) = Build(statement + " " + KeyDefinition);

        var diagnostic = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementsMayComeFromFilter).Subject;
        diagnostic.SourceLocation.Should().Be(Line);
        diagnostic.Subjects.Should().Equal(method, filter);
    }

    [Fact]
    public void FilterNextToARequirement_IsNotReported()
    {
        var (document, diagnostics) = Build(
            "c.OperationFilter<SecurityRequirementsFilter>(); " + KeyDefinition +
            " c.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(\"Key\", document)] = [] });");

        JsonNode.DeepEquals(document["security"], JsonNode.Parse("""[{"Key":[]}]""")).Should().BeTrue();
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SecurityRequirementsMayComeFromFilter);
    }
}
