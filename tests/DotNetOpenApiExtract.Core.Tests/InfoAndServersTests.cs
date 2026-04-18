using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Tests for the Info block (Contact, License, TermsOfService) and Servers fields
/// introduced via <see cref="OpenApiDocumentOptions"/>.
/// </summary>
public class InfoAndServersTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static OpenApiDocument Build(OpenApiDocumentOptions options)
        => OpenApiDocumentBuilder.Build(options);

    // =========================================================================
    // Contact
    // =========================================================================

    [Fact]
    public void Info_Contact_AllThreeFields_EmittedInDocument()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            ContactName  = "Jane Doe",
            ContactEmail = "jane@example.com",
            ContactUrl   = "https://example.com/contact",
        });

        doc.Info.Contact.Should().NotBeNull();
        doc.Info.Contact!.Name.Should().Be("Jane Doe");
        doc.Info.Contact.Email.Should().Be("jane@example.com");
        doc.Info.Contact.Url.Should().NotBeNull();
        doc.Info.Contact.Url!.ToString().Should().Be("https://example.com/contact");
    }

    [Fact]
    public void Info_Contact_OnlyName_EmittedWithoutOthers()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            ContactName  = "Support Team",
        });

        doc.Info.Contact.Should().NotBeNull();
        doc.Info.Contact!.Name.Should().Be("Support Team");
        doc.Info.Contact.Email.Should().BeNull();
        doc.Info.Contact.Url.Should().BeNull();
    }

    [Fact]
    public void Info_Contact_NoneSet_NoContactInDocument()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        });

        doc.Info.Contact.Should().BeNull();
    }

    [Fact]
    public void Info_Contact_InvalidUrl_SkipsUrlButKeepsContact()
    {
        // When the URL is invalid the contact is still created with Name and Email;
        // only the URL field is omitted (best-effort: keep what is valid).
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            ContactName  = "Support",
            ContactEmail = "support@example.com",
            ContactUrl   = "not a url",
        });

        doc.Info.Contact.Should().NotBeNull();
        doc.Info.Contact!.Name.Should().Be("Support");
        doc.Info.Contact.Email.Should().Be("support@example.com");
        doc.Info.Contact.Url.Should().BeNull();
    }

    // =========================================================================
    // License
    // =========================================================================

    [Fact]
    public void Info_License_NameAndUrl_Emitted()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            LicenseName  = "MIT",
            LicenseUrl   = "https://opensource.org/licenses/MIT",
        });

        doc.Info.License.Should().NotBeNull();
        doc.Info.License!.Name.Should().Be("MIT");
        doc.Info.License.Url.Should().NotBeNull();
        doc.Info.License.Url!.ToString().Should().Be("https://opensource.org/licenses/MIT");
    }

    [Fact]
    public void Info_License_OnlyName_Emitted()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            LicenseName  = "Apache-2.0",
        });

        doc.Info.License.Should().NotBeNull();
        doc.Info.License!.Name.Should().Be("Apache-2.0");
        doc.Info.License.Url.Should().BeNull();
    }

    [Fact]
    public void Info_License_OnlyUrl_LicenseNotEmitted()
    {
        // OpenAPI requires license.name; without it the whole license block must be skipped.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            LicenseUrl   = "https://opensource.org/licenses/MIT",
        });

        doc.Info.License.Should().BeNull();
    }

    // =========================================================================
    // Terms of Service
    // =========================================================================

    [Fact]
    public void Info_TermsOfService_Valid_Emitted()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath    = TestPaths.SampleApiDll,
            TermsOfService  = "https://example.com/tos",
        });

        doc.Info.TermsOfService.Should().NotBeNull();
        doc.Info.TermsOfService!.ToString().Should().Be("https://example.com/tos");
    }

    [Fact]
    public void Info_TermsOfService_Invalid_Skipped()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.SampleApiDll,
            TermsOfService = "not a url",
        });

        doc.Info.TermsOfService.Should().BeNull();
    }

    // =========================================================================
    // Servers
    // =========================================================================

    [Fact]
    public void Servers_MultipleUrls_AllEmitted()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Servers      = new[] { "https://api.example.com", "https://staging.example.com" },
        });

        doc.Servers.Should().NotBeNull();
        doc.Servers!.Count.Should().Be(2);
        doc.Servers.Select(s => s.Url).Should().ContainInOrder(
            "https://api.example.com",
            "https://staging.example.com");
    }

    [Fact]
    public void Servers_Empty_NoServersInDocument()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Servers      = Array.Empty<string>(),
        });

        // An empty list results in no servers block (null or empty collection).
        var count = doc.Servers?.Count ?? 0;
        count.Should().Be(0);
    }

    [Fact]
    public void Servers_WhitespaceEntries_Filtered()
    {
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Servers      = new[] { "", "   ", "https://valid.com" },
        });

        doc.Servers.Should().NotBeNull();
        doc.Servers!.Count.Should().Be(1);
        doc.Servers[0].Url.Should().Be("https://valid.com");
    }

    [Fact]
    public void Servers_DuplicateUrls_BothKept()
    {
        // The builder does NOT deduplicate server URLs — both entries are forwarded
        // verbatim. This test documents and locks in that behaviour.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Servers      = new[] { "https://api.example.com", "https://api.example.com" },
        });

        doc.Servers.Should().NotBeNull();
        doc.Servers!.Count.Should().Be(2,
            "duplicate URLs are NOT deduplicated — the caller is responsible for uniqueness");
    }

    // =========================================================================
    // Contact — empty email boundary
    // =========================================================================

    [Fact]
    public void Info_Contact_EmptyEmail_ContactCreatedWithEmptyEmail()
    {
        // ContactEmail == "" is != null, so the Contact block is created.
        // No format validation is performed on the email field.
        var doc = Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            ContactEmail = "",
        });

        doc.Info.Contact.Should().NotBeNull(
            "a non-null ContactEmail (even empty string) triggers Contact creation");
        doc.Info.Contact!.Email.Should().Be("");
    }

    // =========================================================================
    // Regression: baseline unchanged when no new fields are set
    // =========================================================================

    [Fact]
    public void Info_NoNewFieldsSet_BehaviorUnchanged()
    {
        var baselineOptions = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            Title        = "MyApi",
            Version      = "v2",
            Description  = "Test description",
        };

        var doc = Build(baselineOptions);

        doc.Info.Title.Should().Be("MyApi");
        doc.Info.Version.Should().Be("v2");
        doc.Info.Description.Should().Be("Test description");
        doc.Info.Contact.Should().BeNull();
        doc.Info.License.Should().BeNull();
        doc.Info.TermsOfService.Should().BeNull();
        // Servers is not populated when the option is not set; the library may initialise the
        // collection to an empty instance rather than null, so we check count, not null.
        (doc.Servers?.Count ?? 0).Should().Be(0);
    }
}
