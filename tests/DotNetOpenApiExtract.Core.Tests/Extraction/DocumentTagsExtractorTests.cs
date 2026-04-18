using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="DocumentTagsExtractor"/>.
/// </summary>
public class DocumentTagsExtractorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Empty context → empty result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ContextUnavailable_ReturnsEmpty()
    {
        var result = DocumentTagsExtractor.Extract(SourceAnalysisContext.Empty);

        result.TagsByName.Should().BeEmpty();
        result.ExternalDocsUrl.Should().BeNull();
        result.ExternalDocsDescription.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. AddTag with literal Name and Description
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddTag_LiteralName_Extracted()
    {
        var source = """
            c.AddTag(new OpenApiTag { Name = "Users", Description = "User endpoints" });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().ContainKey("Users");
        result.TagsByName["Users"].Description.Should().Be("User endpoints");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. AddTag with ExternalDocs
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddTag_WithExternalDocs()
    {
        var source = """
            c.AddTag(new OpenApiTag
            {
                Name = "Users",
                Description = "User management",
                ExternalDocs = new OpenApiExternalDocs
                {
                    Url = new Uri("https://docs.example.com/users"),
                    Description = "API user guide"
                }
            });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().ContainKey("Users");
        var tag = result.TagsByName["Users"];
        tag.Description.Should().Be("User management");
        tag.ExternalDocsUrl.Should().Be("https://docs.example.com/users");
        tag.ExternalDocsDescription.Should().Be("API user guide");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Multiple AddTag calls — all collected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_MultipleAddTag_AllCollected()
    {
        var source = """
            c.AddTag(new OpenApiTag { Name = "Users", Description = "User endpoints" });
            c.AddTag(new OpenApiTag { Name = "Orders", Description = "Order management" });
            c.AddTag(new OpenApiTag { Name = "Products", Description = "Product catalog" });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().HaveCount(3);
        result.TagsByName.Should().ContainKey("Users");
        result.TagsByName.Should().ContainKey("Orders");
        result.TagsByName.Should().ContainKey("Products");
        result.TagsByName["Orders"].Description.Should().Be("Order management");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Root-level externalDocs from SwaggerDoc
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_RootExternalDocs_Extracted()
    {
        var source = """
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "My API",
                Version = "v1",
                ExternalDocs = new OpenApiExternalDocs
                {
                    Url = new Uri("https://docs.example.com"),
                    Description = "Full API documentation"
                }
            });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.ExternalDocsUrl.Should().Be("https://docs.example.com");
        result.ExternalDocsDescription.Should().Be("Full API documentation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Non-literal tag Name is skipped (variable or expression)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NonLiteralTagName_Skipped()
    {
        var source = """
            var tagName = GetTagName();
            c.AddTag(new OpenApiTag { Name = tagName, Description = "Some description" });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().BeEmpty(
            because: "tag with a non-literal Name cannot be resolved statically");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. No tag registrations → empty result (no crash)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NoTagRegistrations_ReturnsEmpty()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().BeEmpty();
        result.ExternalDocsUrl.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Duplicate AddTag with same name → first-wins
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_DuplicateAddTag_SameName_FirstWins()
    {
        var source = """
            c.AddTag(new OpenApiTag { Name = "Users", Description = "First description" });
            c.AddTag(new OpenApiTag { Name = "Users", Description = "Second description" });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().HaveCount(1,
            because: "duplicate tag names are deduplicated with first-wins semantics");
        result.TagsByName["Users"].Description.Should().Be("First description");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. AddTag without Name property → skipped gracefully
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddTag_NoNameProperty_Skipped()
    {
        var source = """
            c.AddTag(new OpenApiTag { Description = "Orphaned description" });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().BeEmpty(
            because: "a tag without a Name cannot be keyed in the result dictionary");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. AddTag with only ExternalDocs URL (no description on tag)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddTag_ExternalDocsUrlOnly_Extracted()
    {
        var source = """
            c.AddTag(new OpenApiTag
            {
                Name = "Products",
                ExternalDocs = new OpenApiExternalDocs
                {
                    Url = new Uri("https://docs.example.com/products")
                }
            });
            """;

        var context = BuildContext(source);
        var result = DocumentTagsExtractor.Extract(context);

        result.TagsByName.Should().ContainKey("Products");
        var tag = result.TagsByName["Products"];
        tag.Description.Should().BeNull();
        tag.ExternalDocsUrl.Should().Be("https://docs.example.com/products");
        tag.ExternalDocsDescription.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SourceAnalysisContext"/> from an inline source string using
    /// a top-level statement compilation so that <see cref="InvocationMatcher.FindInvocations"/>
    /// can traverse the full tree.
    /// </summary>
    private static SourceAnalysisContext BuildContext(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult("/inline", compilation, [tree]);
        var root = ((CSharpSyntaxTree)tree).GetCompilationUnitRoot();

        return new SourceAnalysisContext(compilationResult, root);
    }
}
