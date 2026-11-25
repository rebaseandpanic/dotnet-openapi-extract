using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
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

    private const string TwoDocuments = """
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1", Contact = new OpenApiContact { Name = "First team" } });
            c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2", Description = "Only in the second", Contact = new OpenApiContact { Name = "Second team", Email = "second@example.com", Url = new Uri("https://second.example.com/contact") } });
        });
        """;

    private const string TwoAddOpenApiDocuments = """
        builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { Title = "Public", Version = "1" };
            return Task.CompletedTask;
        }));
        builder.Services.AddOpenApi("internal", o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { Title = "Internal", Version = "9", Description = "Internal only" };
            return Task.CompletedTask;
        }));
        """;

    private const string OneDocumentInTwoCalls = """
        const string Public = "public";
        builder.Services.AddSwaggerGen(c => c.SwaggerDoc(Public, new OpenApiInfo { Title = "Public", Version = "1" }));
        builder.Services.AddSwaggerGen(c => c.SwaggerDoc("other", new OpenApiInfo { Title = "Other", Version = "2", Description = "Other document" }));
        builder.Services.AddSwaggerGen(c => c.SwaggerDoc(name: "public", info: new OpenApiInfo { Description = "Public description" }));
        """;

    private const string NameNotAConstant = """
        var names = new[] { "v1", "v2" };
        builder.Services.AddSwaggerGen(c =>
        {
            foreach (var name in names)
                c.SwaggerDoc(name, new OpenApiInfo { Title = "Each", Version = "1" });
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Named", Version = "2", Description = "Named description" });
        });
        """;

    private const string AddOpenApiBeforeSwaggerDoc = """
        builder.Services.AddOpenApi("internal", o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { Title = "Internal", Version = "9" };
            return Task.CompletedTask;
        }));
        builder.Services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo { Title = "Public", Version = "1", Description = "Public description" }));
        """;

    private const string ChainedAddOpenApi = """
        builder.Services
            .AddOpenApi("public", o => o.AddDocumentTransformer((document, context, ct) =>
            {
                document.Info = new() { Title = "Public", Version = "1" };
                return Task.CompletedTask;
            }))
            .AddOpenApi("operator", o => o.AddDocumentTransformer((document, context, ct) =>
            {
                document.Info = new() { Title = "Operator", Version = "2", Description = "Operator description" };
                return Task.CompletedTask;
            }));
        """;

    private const string OptionsAsAMethodGroup = """
        builder.Services.AddOpenApi(ConfigureOpenApi);
        builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { Title = "Inline", Version = "4" };
            return Task.CompletedTask;
        }));

        static void ConfigureOpenApi(OpenApiOptions options) { }
        """;

    private const string NameInUpperCase = """
        builder.Services.AddOpenApi("V1", o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { Title = "Upper" };
            return Task.CompletedTask;
        }));
        builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { Version = "7" };
            return Task.CompletedTask;
        }));
        """;

    private const string StaticExtensionCall = """
        builder.Services.AddSwaggerGen(c =>
        {
            SwaggerGenOptionsExtensions.SwaggerDoc(c, "v1", new OpenApiInfo { Title = "Static", Version = "1" });
            c.SwaggerDoc(@name: "v1", info: new OpenApiInfo { Description = "Same document" });
        });
        """;

    private const string LicenseInTwoCalls = """
        builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { Title = "Licensed", Version = "1", License = new OpenApiLicense { Name = "MIT" } };
            return Task.CompletedTask;
        }));
        builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer((document, context, ct) =>
        {
            document.Info = new() { License = new OpenApiLicense { Name = "Other", Url = new Uri("https://example.com/license") } };
            return Task.CompletedTask;
        }));
        """;

    private const string ExternalDocsInTwoDocuments = """
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1", ExternalDocs = new OpenApiExternalDocs { Url = new Uri("https://first.example.com/docs") } });
            c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2", ExternalDocs = new OpenApiExternalDocs { Url = new Uri("https://second.example.com/docs") } });
        });
        """;

    private const string ExternalDocsOnlyInTheSecond = """
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "First", Version = "1" });
            c.SwaggerDoc("v2", new OpenApiInfo { Title = "Second", Version = "2", ExternalDocs = new OpenApiExternalDocs { Url = new Uri("https://second.example.com/docs") } });
        });
        """;

    public static TheoryData<string, string, string> DeclarationsInProgramCs => new()
    {
        // Program.cs → field → the value written
        { TwoDocuments, "/info/title", "First" },
        { TwoDocuments, "/info/contact/name", "First team" },
        { TwoAddOpenApiDocuments, "/info/version", "1" },
        { OneDocumentInTwoCalls, "/info/title", "Public" },
        { OneDocumentInTwoCalls, "/info/description", "Public description" },
        { NameNotAConstant, "/info/title", "Each" },
        // the first declared in the source, whichever method declares it
        { AddOpenApiBeforeSwaggerDoc, "/info/title", "Internal" },
        { ChainedAddOpenApi, "/info/title", "Public" },
        // AddOpenApi(options) declares v1, as AddOpenApi("v1", options) does
        { OptionsAsAMethodGroup, "/info/title", "Inline" },
        // ASP.NET Core registers "V1" as "v1": the unnamed call declares the same document
        { NameInUpperCase, "/info/title", "Upper" },
        { NameInUpperCase, "/info/version", "7" },
        // SwaggerGenOptionsExtensions.SwaggerDoc(c, "v1", …) and @name: "v1" name the same document
        { StaticExtensionCall, "/info/title", "Static" },
        { StaticExtensionCall, "/info/description", "Same document" },
        // the license name from the first call, the URL from the first call that gives a link
        { LicenseInTwoCalls, "/info/license/name", "MIT" },
        { LicenseInTwoCalls, "/info/license/url", "https://example.com/license" },
        { ExternalDocsInTwoDocuments, "/externalDocs/url", "https://first.example.com/docs" },
        { """
          builder.Services.AddOpenApi(o => o.AddDocumentTransformer((document, context, ct) =>
          {
              document.Info = new() { Title = "Transformed", Version = "3.1.4" };
              return Task.CompletedTask;
          }));
          """, "/info/version", "3.1.4" },
    };

    /// <summary>
    /// Several declarations of the document built — the first one declared — : each field from the first
    /// that sets it, the contact field by field; <c>AddOpenApi</c> is read as <c>SwaggerDoc</c> is, a document
    /// name given as a constant is read as a literal.
    /// </summary>
    [Theory]
    [MemberData(nameof(DeclarationsInProgramCs))]
    public void InfoField_ComesFromTheFirstDeclarationOfTheDocumentThatSetsIt(string program, string pointer, string expected)
    {
        var (document, _) = ConfigFormsBuild.BuildWithSources(("Program.cs", program));

        ShouldHave(document, pointer, expected);
    }

    public static TheoryData<string, string> FieldsOfAnotherDocument => new()
    {
        // Program.cs → a field only another document sets
        { TwoDocuments, "/info/description" },
        { TwoDocuments, "/info/contact/email" },
        { TwoDocuments, "/info/contact/url" },
        { TwoAddOpenApiDocuments, "/info/description" },
        // a name that is not a constant is a document of its own, not the "v1" after it
        { NameNotAConstant, "/info/description" },
        { AddOpenApiBeforeSwaggerDoc, "/info/description" },
        { ChainedAddOpenApi, "/info/description" },
        { ExternalDocsOnlyInTheSecond, "/externalDocs" },
    };

    /// <summary>
    /// The info of another document never fills a field of the document built: the document would describe
    /// the API of the other one (doe-83: the description of an operator API in the document of the public one).
    /// </summary>
    [Theory]
    [MemberData(nameof(FieldsOfAnotherDocument))]
    public void InfoField_OfAnotherDocument_IsNotRead(string program, string pointer)
    {
        var (document, _) = ConfigFormsBuild.BuildWithSources(("Program.cs", program));

        ConfigFormsBuild.At(document, pointer).Should().BeNull(pointer);
    }

    public static TheoryData<string, bool> UnreadDescription => new()
    {
        // Program.cs → whether the description that cannot be read is reported
        { """
          builder.Services.AddSwaggerGen(c =>
          {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "Public", Version = "1", Description = "Public description" });
              c.SwaggerDoc("operator", new OpenApiInfo { Title = "Operator", Version = "1", Description = OperatorDescription() });
          });
          static string OperatorDescription() => System.Environment.GetEnvironmentVariable("OPERATOR") ?? "";
          """, false },
        { """
          builder.Services.AddSwaggerGen(c =>
          {
              c.SwaggerDoc("v1", new OpenApiInfo { Title = "Public", Version = "1", Description = PublicDescription() });
              c.SwaggerDoc("operator", new OpenApiInfo { Title = "Operator", Version = "1", Description = "Operator description" });
          });
          static string PublicDescription() => System.Environment.GetEnvironmentVariable("PUBLIC") ?? "";
          """, true },
    };

    /// <summary>
    /// A value of another document that cannot be read is no loss for the document built, and <c>--description</c>
    /// would set it on the wrong document: only the declarations of the document built are reported.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnreadDescription))]
    public void UnreadInfoValue_IsReportedForTheDocumentBuiltOnly(string program, bool reported)
    {
        var (_, diagnostics) = ConfigFormsBuild.BuildWithSources(("Program.cs", program));

        diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.DocumentMetadataNotStatic)
            .Should().HaveCount(reported ? 1 : 0);
    }
}
