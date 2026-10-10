using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Metadata;

/// <summary>
/// Title, description and the contact name come from the assembly attributes the project set before the
/// <c>OpenApiInfo</c> of <c>SwaggerDoc</c>, as in 0.16; an attribute equal to the assembly name is the
/// MSBuild default and gives way to SwaggerDoc. A difference between a set attribute and SwaggerDoc is
/// reported. Version and terms of service, which have no attribute, come from SwaggerDoc.
/// SampleApi sets <c>&lt;AssemblyTitle&gt;</c>, <c>&lt;Description&gt;</c>, <c>&lt;Product&gt;</c> and
/// <c>&lt;Company&gt;</c>; ModernApi has only the MSBuild defaults.
/// </summary>
public class InfoAttributePrecedenceTests
{
    private const string WithSwaggerDoc = """
        builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Doc title",
            Version = "3.0",
            Description = "Doc description",
            Contact = new OpenApiContact { Name = "Doc team", Email = "doc@example.com", Url = new Uri("https://doc.example.com/contact") },
            TermsOfService = new Uri("https://doc.example.com/terms"),
        }));
        """;

    private const string SameAsSampleApiAttributes = """
        builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Sample API Title",
            Version = "v1",
            Description = "Sample API description for testing info.description auto-default",
            Contact = new OpenApiContact { Name = "Acme Corp" },
        }));
        """;

    private const string WithoutSwaggerDoc = "builder.Services.AddSwaggerGen();";

    private static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(string fixture, string program)
    {
        using var sourceRoot = new TempDirectory();
        File.WriteAllText(Path.Combine(sourceRoot.Path, "Program.cs"), program);
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = fixture == "SampleApi" ? TestPaths.SampleApiDll : TestPaths.ModernApiDll,
            SourceRoot     = sourceRoot.Path,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_1,
            OnDiagnostic   = onDiagnostic,
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    public static TheoryData<string, string, string, string> Fields => new()
    {
        // fixture → Program.cs → field → the value written
        { "SampleApi", WithSwaggerDoc, "/info/title", "Sample API Title" },
        { "SampleApi", WithSwaggerDoc, "/info/description", "Sample API description for testing info.description auto-default" },
        { "SampleApi", WithSwaggerDoc, "/info/contact/name", "Acme Corp" },
        { "SampleApi", WithSwaggerDoc, "/info/contact/email", "doc@example.com" },
        { "SampleApi", WithSwaggerDoc, "/info/contact/url", "https://doc.example.com/contact" },
        { "SampleApi", WithSwaggerDoc, "/info/version", "3.0" },
        { "SampleApi", WithSwaggerDoc, "/info/termsOfService", "https://doc.example.com/terms" },
        // MSBuild defaults (the assembly name) give way to SwaggerDoc
        { "ModernApi", WithSwaggerDoc, "/info/title", "Doc title" },
        { "ModernApi", WithSwaggerDoc, "/info/contact/name", "Doc team" },
        { "ModernApi", WithSwaggerDoc, "/info/description", "Doc description" },
        // without SwaggerDoc, as in 0.16: the MSBuild defaults, then v1
        { "ModernApi", WithoutSwaggerDoc, "/info/title", "ModernApi" },
        { "ModernApi", WithoutSwaggerDoc, "/info/contact/name", "ModernApi" },
        { "ModernApi", WithoutSwaggerDoc, "/info/version", "v1" },
    };

    [Theory]
    [MemberData(nameof(Fields))]
    public void InfoField_ComesFromTheAttributeTheProjectSet_ThenSwaggerDoc(string fixture, string program, string pointer, string expected)
    {
        var (document, _) = Build(fixture, program);

        var node = ConfigFormsBuild.At(document, pointer);
        node.Should().NotBeNull(pointer);
        node!.GetValue<string>().Should().Be(expected, pointer);
    }

    [Fact]
    public void SetAttributeAndSwaggerDoc_Differing_AreReported_PerField_WithThePlaceInSwaggerDoc()
    {
        var (_, diagnostics) = Build("SampleApi", WithSwaggerDoc);

        var warnings = diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.DocumentInfoSourcesDiffer).ToList();
        warnings.Select(w => w.Subjects[0]).Should().BeEquivalentTo(["info.title", "info.description", "info.contact.name"]);
        warnings.Should().AllSatisfy(w => w.SourceLocation.Should().StartWith("Program.cs:"));
    }

    public static TheoryData<string, string> NoDifference => new()
    {
        { "SampleApi", SameAsSampleApiAttributes },
        { "ModernApi", WithSwaggerDoc },
    };

    /// <summary>Equal values, or an attribute that is only the MSBuild default, are no difference to report.</summary>
    [Theory]
    [MemberData(nameof(NoDifference))]
    public void EqualValues_OrMsBuildDefaults_AreNotReported(string fixture, string program)
    {
        var (_, diagnostics) = Build(fixture, program);

        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.DocumentInfoSourcesDiffer);
    }
}
