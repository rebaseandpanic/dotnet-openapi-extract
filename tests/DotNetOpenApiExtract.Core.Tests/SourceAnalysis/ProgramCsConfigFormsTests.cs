using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

/// <summary>Builds a document from a compiled fixture, or from a fixture DLL with a Program.cs of the test's own.</summary>
internal static class ConfigFormsBuild
{
    public static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(
        string assemblyPath, string? sourceRoot, OpenApiSpecVersion version = OpenApiSpecVersion.OpenApi3_1)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = assemblyPath,
            SourceRoot     = sourceRoot,
            OpenApiVersion = version,
            OnDiagnostic   = onDiagnostic,
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    /// <summary>Builds ModernApi with <paramref name="files"/> (file name → text) as its sources.</summary>
    public static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) BuildWithSources(
        params (string Name, string Text)[] files)
    {
        using var directory = new TempDirectory();
        foreach (var (name, text) in files)
            File.WriteAllText(Path.Combine(directory.Path, name), text);
        return Build(TestPaths.ModernApiDll, directory.Path);
    }

    /// <summary>The node at the JSON pointer <paramref name="pointer"/> (<c>/a/b/0</c>), or <see langword="null"/>.</summary>
    public static JsonNode? At(JsonNode document, string pointer)
    {
        var node = document;
        foreach (var segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node switch
            {
                JsonArray array when int.TryParse(segment, out var index) => index < array.Count ? array[index] : null,
                JsonObject obj => obj[segment.Replace("~1", "/").Replace("~0", "~")],
                _ => null,
            };
            if (node == null)
                return null;
        }

        return node;
    }

    /// <summary>The codes of the warnings about configuration the extractor could not read.</summary>
    public static readonly string[] NotStaticCodes =
    [
        ExtractionDiagnosticCodes.DocumentMetadataNotStatic,
        ExtractionDiagnosticCodes.SecuritySchemeNotStatic,
        ExtractionDiagnosticCodes.SecuritySchemeFieldNotStatic,
        ExtractionDiagnosticCodes.SecurityDefinitionNonLiteralName,
        ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScheme,
        ExtractionDiagnosticCodes.SecurityRequirementNonLiteralScopes,
        ExtractionDiagnosticCodes.SecurityRequirementNotStatic,
        ExtractionDiagnosticCodes.SecurityRequirementsMayComeFromFilter,
    ];
}

/// <summary>The ConfigFormsApi fixture (Swashbuckle 10), built once for OpenAPI 3.1.</summary>
public sealed class ConfigFormsApiFixture
{
    public ConfigFormsApiFixture() => (Document, Diagnostics) = ConfigFormsBuild.Build(TestPaths.ConfigFormsApiDll, sourceRoot: null);

    public JsonNode Document { get; }

    public IReadOnlyList<ExtractionDiagnostic> Diagnostics { get; }
}

/// <summary>
/// The forms a Swashbuckle 10 Program.cs uses for the document metadata and security give the values
/// Swashbuckle serves, without a warning: the way the code is written does not change the document.
/// The fixture is compiled, so every form is code Swashbuckle 10 accepts.
/// </summary>
public class ProgramCsConfigFormsTests(ConfigFormsApiFixture fixture) : IClassFixture<ConfigFormsApiFixture>
{
    public static TheoryData<string, string, string> Swashbuckle10Forms => new()
    {
        // form → where it lands → the value Swashbuckle serves
        { "target-typed info with a nested target-typed contact, summary by literal concatenation", "/info/summary", "\"Summary joined\"" },
        { "title by literal concatenation, over the MSBuild default [AssemblyTitle]", "/info/title", "\"Config forms\"" },
        { "version by interpolation of a constant", "/info/version", "\"2.0\"" },
        { "description by a raw string", "/info/description", "\"A raw\\ndescription\"" },
        { "contact name by nameof, email from a const of another class, URL by new Uri, over the MSBuild default [AssemblyCompany]", "/info/contact",
          """{"name":"Team","url":"https://contact.example.com","email":"team@example.com"}""" },
        { "terms of service by a target-typed Uri from a const of another class", "/info/termsOfService", "\"https://terms.example.com/v2\"" },
        { "license name by interpolation of constants, URL from a const of another class", "/info/license",
          """{"name":"Forms License","url":"https://license.example.com/terms"}""" },
        { "Microsoft.OpenApi.* type names, header name from a const of another class, raw string description", "/components/securitySchemes/KeyHeader",
          """{"type":"apiKey","description":"Key in a header","name":"X-Header-Key","in":"header"}""" },
        { "cookie name from a const of another class, description by literal concatenation", "/components/securitySchemes/KeyCookie",
          """{"type":"apiKey","description":"Key in a cookie","name":"session-key","in":"cookie"}""" },
        { "scheme id by nameof, name by interpolation of constants", "/components/securitySchemes/Partner",
          """{"type":"apiKey","name":"Forms-partner","in":"query"}""" },
        { "requirement by collection initializer with a 3-argument reference, new List<string>() scopes", "/security/0", """{"KeyHeader":[]}""" },
        { "requirement by index initializer with a 2-argument reference, [] scopes", "/security/1", """{"KeyCookie":[]}""" },
        { "requirement in a block-bodied lambda, two entries, nameof id", "/security/2", """{"Bearer":[],"Partner":[]}""" },
    };

    [Theory]
    [MemberData(nameof(Swashbuckle10Forms))]
    public void Swashbuckle10Form_IsRead(string form, string pointer, string expected)
    {
        var actual = ConfigFormsBuild.At(fixture.Document, pointer);

        actual.Should().NotBeNull(form);
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue($"{form}: {actual!.ToJsonString()}");
    }

    [Fact]
    public void Swashbuckle10Forms_GiveNoWarning()
    {
        fixture.Diagnostics.Where(d => ConfigFormsBuild.NotStaticCodes.Contains(d.Code))
            .Should().BeEmpty();
    }
}

/// <summary>The Swashbuckle 6–9 forms (Microsoft.OpenApi 1.x), read from a Program.cs given as text.</summary>
public sealed class Swashbuckle9FormsFixture
{
    private const string Program = """
        using Microsoft.OpenApi.Models;

        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Nine",
                Version = "v1",
                License = new Microsoft.OpenApi.Models.OpenApiLicense { Name = "Nine " + "License" },
            });
            c.AddSecurityDefinition("A", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Name = "X-A",
            });
            c.AddSecurityDefinition("B", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-B" });
            c.AddSecurityDefinition("C", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-C" });
            c.AddSecurityDefinition("D", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-D" });
            c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "A" },
                    },
                    Array.Empty<string>()
                },
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "B" } }, new string[0] },
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "C", Type = ReferenceType.SecurityScheme } }, new string[] { } },
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "D" } }, new List<string>() },
            });
        });
        """;

    public Swashbuckle9FormsFixture() => (Document, Diagnostics) = ConfigFormsBuild.BuildWithSources(("Program.cs", Program));

    public JsonNode Document { get; }

    public IReadOnlyList<ExtractionDiagnostic> Diagnostics { get; }
}

/// <summary>
/// The Swashbuckle 6–9 forms: <c>Microsoft.OpenApi.Models.*</c> type names, the requirement key
/// <c>Reference = new OpenApiReference { … }</c> and the empty scopes of that API generation.
/// </summary>
public class Swashbuckle9ConfigFormsTests(Swashbuckle9FormsFixture fixture) : IClassFixture<Swashbuckle9FormsFixture>
{
    public static TheoryData<string, string, string> Swashbuckle9Forms => new()
    {
        { "Microsoft.OpenApi.Models.OpenApiInfo and OpenApiLicense", "/info/license", """{"name":"Nine License"}""" },
        { "Microsoft.OpenApi.Models.OpenApiSecurityScheme with its enums", "/components/securitySchemes/A", """{"type":"apiKey","name":"X-A","in":"header"}""" },
        { "Microsoft.OpenApi.Models reference, Array.Empty<string>() scopes", "/security/0", """{"A":[]}""" },
        { "OpenApiReference, new string[0] scopes", "/security/1", """{"B":[]}""" },
        { "OpenApiReference with Id first, new string[] { } scopes", "/security/2", """{"C":[]}""" },
        { "OpenApiReference, new List<string>() scopes", "/security/3", """{"D":[]}""" },
    };

    [Theory]
    [MemberData(nameof(Swashbuckle9Forms))]
    public void Swashbuckle9Form_IsRead(string form, string pointer, string expected)
    {
        var actual = ConfigFormsBuild.At(fixture.Document, pointer);

        actual.Should().NotBeNull(form);
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).Should().BeTrue($"{form}: {actual!.ToJsonString()}");
    }

    [Fact]
    public void Swashbuckle9Forms_GiveNoWarning()
    {
        fixture.Diagnostics.Where(d => ConfigFormsBuild.NotStaticCodes.Contains(d.Code))
            .Should().BeEmpty();
    }
}

/// <summary>
/// Interpolated strings evaluate as C# evaluates them: <c>{{</c> / <c>}}</c> are one brace in a regular
/// or verbatim interpolation, a raw interpolation takes its braces literally outside the holes.
/// </summary>
public class InterpolatedStringFormsTests
{
    public static TheoryData<string, string> Interpolations => new()
    {
        // the Summary expression → the info.summary Swashbuckle serves
        { "$\"{{{\"a\"}}}\"", "{a}" },
        { "$@\"{{{\"a\"}}}\"", "{a}" },
        { "$\"x{{y}}{\"z\"}\"", "x{y}z" },
        { "$$\"\"\"{x} {{\"a\"}}\"\"\"", "{x} a" },
    };

    [Theory]
    [MemberData(nameof(Interpolations))]
    public void InterpolatedSummary_IsTheCSharpValue(string expression, string expected)
    {
        var (document, diagnostics) = ConfigFormsBuild.BuildWithSources(("Program.cs",
            $"builder.Services.AddSwaggerGen(c => c.SwaggerDoc(\"v1\", new() {{ Title = \"T\", Summary = {expression} }}));"));

        document["info"]?["summary"]?.GetValue<string>().Should().Be(expected);
        document["info"]!.AsObject().ContainsKey("summary").Should().BeTrue();
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.DocumentMetadataNotStatic);
    }
}
