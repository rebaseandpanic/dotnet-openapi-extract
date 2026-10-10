using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Metadata;

/// <summary>
/// Every <c>info</c> field comes from its first source, field by field: the option (CLI flag) first, then
/// the <c>OpenApiInfo</c> of <c>SwaggerDoc</c> / <c>AddOpenApi</c> in Program.cs where no assembly attribute
/// set by the project gives the field (ConfigFormsApi has only the MSBuild defaults). The values of the
/// ConfigFormsApi Program.cs are those of <see cref="ProgramCsConfigFormsTests"/>.
/// </summary>
public class InfoSourcePrecedenceTests
{
    /// <summary>ConfigFormsApi with the option named <paramref name="option"/> set to <paramref name="value"/>.</summary>
    private static JsonNode BuildWithOption(string option, string value)
    {
        var (document, _) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ConfigFormsApiDll,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_1,
            OnDiagnostic   = onDiagnostic,
            Title          = option == "Title" ? value : null,
            Description    = option == "Description" ? value : null,
            Version        = option == "Version" ? value : null,
            ContactName    = option == "ContactName" ? value : null,
            ContactEmail   = option == "ContactEmail" ? value : null,
            ContactUrl     = option == "ContactUrl" ? value : null,
            TermsOfService = option == "TermsOfService" ? value : null,
        });
        return JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!;
    }

    private static void ShouldHave(JsonNode document, string pointer, string expected)
    {
        var node = ConfigFormsBuild.At(document, pointer);
        node.Should().NotBeNull(pointer);
        node!.GetValue<string>().Should().Be(expected, pointer);
    }

    public static TheoryData<string, string, string, string, string> OptionOverSwaggerDoc => new()
    {
        // option → its value → where it lands; another field of the same SwaggerDoc → the value it keeps
        { "Title", "Flag title", "/info/title", "/info/version", "2.0" },
        { "Description", "Flag description", "/info/description", "/info/title", "Config forms" },
        { "Version", "9.9", "/info/version", "/info/title", "Config forms" },
        { "ContactName", "Flag team", "/info/contact/name", "/info/contact/email", "team@example.com" },
        { "ContactEmail", "flag@example.com", "/info/contact/email", "/info/contact/name", "Team" },
        { "ContactUrl", "https://flag.example.com/contact", "/info/contact/url", "/info/contact/email", "team@example.com" },
        { "TermsOfService", "https://flag.example.com/terms", "/info/termsOfService", "/info/version", "2.0" },
    };

    /// <summary>An option replaces its own field only; every other field still comes from SwaggerDoc.</summary>
    [Theory]
    [MemberData(nameof(OptionOverSwaggerDoc))]
    public void Option_WinsOverSwaggerDoc_ForItsFieldOnly(string option, string value, string pointer, string otherPointer, string otherValue)
    {
        var document = BuildWithOption(option, value);

        ShouldHave(document, pointer, value);
        ShouldHave(document, otherPointer, otherValue);
    }

    public static TheoryData<string, string, string> DeclarationsInProgramCs => new()
    {
        // Program.cs → field → the value written
        { """
          builder.Services.AddSwaggerGen(c =>
          {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1.0" });
              c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2.0", Description = "Only in the second" });
          });
          """, "/info/title", "First" },
        { """
          builder.Services.AddSwaggerGen(c =>
          {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1.0" });
              c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2.0", Description = "Only in the second" });
          });
          """, "/info/description", "Only in the second" },
        { """
          builder.Services.AddSwaggerGen(c =>
          {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1", Contact = new OpenApiContact { Name = "First team" } });
              c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2", Contact = new OpenApiContact { Name = "Second team", Email = "second@example.com", Url = new Uri("https://second.example.com/contact") } });
          });
          """, "/info/contact/name", "First team" },
        { """
          builder.Services.AddSwaggerGen(c =>
          {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1", Contact = new OpenApiContact { Name = "First team" } });
              c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2", Contact = new OpenApiContact { Name = "Second team", Email = "second@example.com", Url = new Uri("https://second.example.com/contact") } });
          });
          """, "/info/contact/email", "second@example.com" },
        { """
          builder.Services.AddSwaggerGen(c =>
          {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1", Contact = new OpenApiContact { Name = "First team" } });
              c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2", Contact = new OpenApiContact { Name = "Second team", Email = "second@example.com", Url = new Uri("https://second.example.com/contact") } });
          });
          """, "/info/contact/url", "https://second.example.com/contact" },
        { """
          builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, context, ct) =>
          {
              document.Info = new() { Title = "Transformed", Version = "3.1.4" };
              return Task.CompletedTask;
          }));
          """, "/info/version", "3.1.4" },
    };

    /// <summary>
    /// Several declarations: each field from the first that sets it, as for <c>info.summary</c> and
    /// <c>externalDocs</c>, the contact field by field; <c>AddOpenApi</c> is read as <c>SwaggerDoc</c> is.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeclarationsInProgramCs))]
    public void InfoField_ComesFromTheFirstDeclarationThatSetsIt(string program, string pointer, string expected)
    {
        var (document, _) = ConfigFormsBuild.BuildWithSources(("Program.cs", program));

        ShouldHave(document, pointer, expected);
    }
}
