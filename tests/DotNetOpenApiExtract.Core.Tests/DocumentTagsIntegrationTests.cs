using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests verifying that document-level tag metadata (descriptions and
/// externalDocs) extracted from Roslyn source is correctly applied to the generated
/// OpenAPI document.
/// </summary>
public class DocumentTagsIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. ApplyDocumentTagsMetadata enriches a tag that has no description
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyDocumentTagsMetadata_EnrichesTagWithNullDescription()
    {
        // Arrange: build a document with a tag that has no description.
        var document = new OpenApiDocument
        {
            Info  = new OpenApiInfo { Title = "Test", Version = "v1" },
            Paths = new OpenApiPaths(),
            Tags  = new HashSet<OpenApiTag>
            {
                new OpenApiTag { Name = "Users", Description = null },
            },
        };

        var docTagsResult = new DocumentTagsExtractionResult
        {
            TagsByName = new Dictionary<string, TagMetadata>(StringComparer.Ordinal)
            {
                ["Users"] = new TagMetadata { Description = "User management" },
            },
        };

        // Act
        OpenApiDocumentBuilder.ApplyDocumentTagsMetadata(document, docTagsResult);

        // Assert
        var usersTag = document.Tags!.First(t => t.Name == "Users");
        usersTag.Description.Should().Be("User management",
            because: "the Roslyn extractor should enrich the tag description when it was null");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Existing [SwaggerTag] description wins over Roslyn AddTag description
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ExistingTagDescription_RoslynDoesNotOverwrite()
    {
        using var tempDir = new TempDirectory();

        // UsersController has [SwaggerTag("User management — CRUD operations for users")]
        // so its tag arrives with a non-null description. The Roslyn AddTag provides
        // a different description — it must NOT overwrite the existing one.
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            c.AddTag(new OpenApiTag { Name = "Users", Description = "from-roslyn" });
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

        document.Tags.Should().NotBeNull();
        var usersTag = document.Tags!.FirstOrDefault(t => t.Name == "Users");
        usersTag.Should().NotBeNull();
        usersTag!.Description.Should().NotBe("from-roslyn",
            because: "existing [SwaggerTag] description must take priority over Roslyn AddTag");
        usersTag.Description.Should().Be("User management — CRUD operations for users",
            because: "the [SwaggerTag] attribute description is the source of truth");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. No document-tags config → document unchanged (no crash, no regression)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_NoDocumentTagsConfig_NoChange()
    {
        // No SourceRoot provided — Roslyn analysis is skipped.
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        // Document should build normally with tags from controllers.
        document.Should().NotBeNull();
        document.Tags.Should().NotBeNullOrEmpty(
            because: "controller tags are always discovered from DLL metadata");
        document.ExternalDocs.Should().BeNull(
            because: "no externalDocs were configured in the source or options");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Root externalDocs from SwaggerDoc → set on document
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_RootExternalDocs_SetOnDocument()
    {
        using var tempDir = new TempDirectory();

        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Sample API",
                Version = "v1",
                ExternalDocs = new OpenApiExternalDocs
                {
                    Url = new Uri("https://docs.example.com/api"),
                    Description = "Complete API reference"
                }
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

        document.ExternalDocs.Should().NotBeNull(
            because: "SwaggerDoc with ExternalDocs should populate document.ExternalDocs");
        document.ExternalDocs!.Url.Should().Be(new Uri("https://docs.example.com/api"));
        document.ExternalDocs.Description.Should().Be("Complete API reference");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. AddTag with ExternalDocs URL → tag ExternalDocs populated
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AddTagWithExternalDocs_TagExternalDocsPopulated()
    {
        using var tempDir = new TempDirectory();

        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            c.AddTag(new OpenApiTag
            {
                Name = "Orders",
                Description = "Order lifecycle management",
                ExternalDocs = new OpenApiExternalDocs
                {
                    Url = new Uri("https://docs.example.com/orders"),
                    Description = "Order management guide"
                }
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

        document.Tags.Should().NotBeNull();
        var ordersTag = document.Tags!.FirstOrDefault(t => t.Name == "Orders");
        ordersTag.Should().NotBeNull(
            because: "SampleApi contains an OrdersController which maps to the 'Orders' tag");
        ordersTag!.ExternalDocs.Should().NotBeNull(
            because: "AddTag with ExternalDocs should populate the tag's ExternalDocs");
        ordersTag.ExternalDocs!.Url.Should().Be(new Uri("https://docs.example.com/orders"));
        ordersTag.ExternalDocs.Description.Should().Be("Order management guide");
    }
}
