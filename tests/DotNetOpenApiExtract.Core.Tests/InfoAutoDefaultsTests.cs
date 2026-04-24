using AwesomeAssertions;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests for the auto-population of <c>info.*</c> fields from assembly-level
/// attributes (<c>[AssemblyDescription]</c>, <c>[AssemblyTitle]</c>, <c>[AssemblyProduct]</c>,
/// <c>[AssemblyCompany]</c>) when the corresponding CLI options are absent.
///
/// SampleApi.csproj declares:
///   AssemblyTitle   = "Sample API Title"
///   Description     = "Sample API description for testing info.description auto-default"
///   Product         = "Sample API Product"
///   Company         = "Acme Corp"
/// MSBuild embeds these as the corresponding <c>[Assembly*Attribute]</c> values.
/// </summary>
public class InfoAutoDefaultsTests
{
    private static OpenApiDocument Build(OpenApiDocumentOptions options)
        => OpenApiDocumentBuilder.Build(options);

    // =========================================================================
    // info.description — auto-default from [AssemblyDescription]
    // =========================================================================

    [Fact]
    public void InfoDescription_NullOption_PopulatedFromAssemblyDescription()
    {
        // options.Description is null → builder must fall back to [AssemblyDescription].
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        });

        doc.Info.Description.Should().Be(
            "Sample API description for testing info.description auto-default",
            "when Description option is absent, [AssemblyDescription] is the source");
    }

    [Fact]
    public void InfoDescription_ExplicitOption_OverridesAssemblyDescription()
    {
        // Explicit options.Description must win over [AssemblyDescription].
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Description  = "explicit description",
        });

        doc.Info.Description.Should().Be("explicit description",
            "explicit CLI option must take precedence over [AssemblyDescription]");
    }

    [Fact]
    public void InfoDescription_WhitespaceOption_TreatedAsAbsent_FallsBackToAssembly()
    {
        // A whitespace-only option is treated as absent; [AssemblyDescription] should be used.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Description  = "   ",
        });

        doc.Info.Description.Should().Be(
            "Sample API description for testing info.description auto-default",
            "whitespace-only Description is rejected; [AssemblyDescription] wins");
    }

    // =========================================================================
    // info.title — auto-default from [AssemblyTitle]
    // =========================================================================

    [Fact]
    public void InfoTitle_NullOption_PopulatedFromAssemblyTitle()
    {
        // options.Title is null → builder must fall back to [AssemblyTitle].
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        });

        doc.Info.Title.Should().Be("Sample API Title",
            "when Title option is absent, [AssemblyTitle] is the source");
    }

    [Fact]
    public void InfoTitle_ExplicitOption_OverridesAssemblyTitle()
    {
        // Explicit options.Title must win over [AssemblyTitle].
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Title        = "Explicit Title",
        });

        doc.Info.Title.Should().Be("Explicit Title",
            "explicit CLI option must take precedence over [AssemblyTitle]");
    }

    [Fact]
    public void InfoTitle_WhitespaceOption_TreatedAsAbsent_FallsBackToAssembly()
    {
        var doc = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            Title        = "   ",
        });

        doc.Info.Title.Should().Be("Sample API Title",
            because: "whitespace Title option is treated as absent and falls back to [AssemblyTitle]");
    }

    // =========================================================================
    // info.contact.name — auto-default from [AssemblyCompany]
    // =========================================================================

    [Fact]
    public void InfoContactName_NullOption_PopulatedFromAssemblyCompany()
    {
        // No contact CLI options are set → builder must create Contact using [AssemblyCompany].
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        });

        doc.Info.Contact.Should().NotBeNull(
            "SampleApi embeds [AssemblyCompany] so Contact block is auto-created");
        doc.Info.Contact!.Name.Should().Be("Acme Corp",
            "[AssemblyCompany] is the source for contact.name when ContactName option is absent");
        doc.Info.Contact.Email.Should().BeNull();
        doc.Info.Contact.Url.Should().BeNull();
    }

    [Fact]
    public void InfoContactName_ExplicitOption_OverridesAssemblyCompany()
    {
        // Explicit options.ContactName must win over [AssemblyCompany].
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            ContactName  = "Explicit Contact",
        });

        doc.Info.Contact.Should().NotBeNull();
        doc.Info.Contact!.Name.Should().Be("Explicit Contact",
            "explicit ContactName must take precedence over [AssemblyCompany]");
    }

    [Fact]
    public void InfoContactName_WhitespaceOption_TreatedAsAbsent_FallsBackToAssembly()
    {
        // Whitespace-only ContactName is treated as absent; [AssemblyCompany] should fill in.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            ContactName  = "   ",
        });

        doc.Info.Contact.Should().NotBeNull();
        doc.Info.Contact!.Name.Should().Be("Acme Corp",
            "whitespace-only ContactName is rejected; [AssemblyCompany] wins");
    }

    // =========================================================================
    // info.title — [AssemblyProduct] fall-back branch
    // =========================================================================
    // Exercises the title-resolution chain step that is unreachable through
    // SampleApi (SampleApi declares [AssemblyTitle]). MinimalProductApi omits
    // [AssemblyTitle] and declares only [AssemblyProduct] + [AssemblyCompany].

    [Fact]
    public void InfoTitle_NoAssemblyTitle_FallsBackToAssemblyProduct()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.MinimalProductApiDll,
        });

        doc.Info.Title.Should().Be("Minimal Product",
            because: "[AssemblyTitle] is absent on MinimalProductApi, so the title " +
                     "resolution chain must land on [AssemblyProduct]");
    }

    [Fact]
    public void InfoDescription_NoAssemblyDescription_IsNull()
    {
        // MinimalProductApi does not declare [AssemblyDescription]; there is no
        // further fall-back, so info.description must stay null.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.MinimalProductApiDll,
        });

        doc.Info.Description.Should().BeNull(
            because: "[AssemblyDescription] is absent and no --description option was supplied");
    }

    [Fact]
    public void InfoContactName_NoContactOptions_PopulatedFromAssemblyCompany_MinimalFixture()
    {
        // MinimalProductApi declares [AssemblyCompany] = "Minimal Co" — Contact block
        // must be created with Name populated from that attribute.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.MinimalProductApiDll,
        });

        doc.Info.Contact.Should().NotBeNull();
        doc.Info.Contact!.Name.Should().Be("Minimal Co");
        doc.Info.Contact.Email.Should().BeNull();
        doc.Info.Contact.Url.Should().BeNull();
    }

    // =========================================================================
    // info.title — assembly-name final fall-back
    // =========================================================================
    // BareMinimalApi declares *no* identity attributes; the chain must reach
    // assembly.GetName().Name (= the DLL file name minus extension).

    [Fact]
    public void InfoTitle_NoIdentityAttributes_FallsBackToAssemblyName()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.BareMinimalApiDll,
        });

        doc.Info.Title.Should().Be("BareMinimalApi",
            because: "when [AssemblyTitle] and [AssemblyProduct] are both absent, " +
                     "the chain falls back to assembly.GetName().Name which matches " +
                     "the DLL file name");
    }

    // =========================================================================
    // Contact gate — no CLI options and no [AssemblyCompany] → no Contact block
    // =========================================================================
    // BareMinimalApi has no [AssemblyCompany]; without CLI contact options the
    // Contact block must not be created. This guards the gate-fix that uses
    // IsNullOrWhiteSpace(options.ContactName) rather than != null.

    [Fact]
    public void InfoContact_NoOptionsAndNoAssemblyCompany_ContactBlockOmitted()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.BareMinimalApiDll,
        });

        doc.Info.Contact.Should().BeNull(
            because: "no ContactName/Email/Url options were set and [AssemblyCompany] " +
                     "is absent, so the Contact block must be omitted entirely");
    }

    [Fact]
    public void InfoContact_WhitespaceContactNameAndNoAssemblyCompany_ContactBlockOmitted()
    {
        // Whitespace-only ContactName must be treated as absent by the gate.
        // Without [AssemblyCompany] to fall back to, no Contact block should be created.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.BareMinimalApiDll,
            ContactName  = "   ",
        });

        doc.Info.Contact.Should().BeNull(
            because: "whitespace-only ContactName is treated as absent by the gate, and " +
                     "BareMinimalApi has no [AssemblyCompany] fall-back");
    }

    [Fact]
    public void InfoContact_EmailOnly_NoAssemblyCompany_ContactCreatedWithEmailOnly()
    {
        // ContactEmail still goes through the != null branch of the gate (preserves
        // the existing EmptyEmail-creates-Contact behaviour even without [AssemblyCompany]).
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.BareMinimalApiDll,
            ContactEmail = "ops@example.com",
        });

        doc.Info.Contact.Should().NotBeNull();
        doc.Info.Contact!.Email.Should().Be("ops@example.com");
        doc.Info.Contact.Name.Should().BeNull(
            because: "no ContactName option and no [AssemblyCompany] means Contact.Name stays null");
        doc.Info.Contact.Url.Should().BeNull();
    }
}
