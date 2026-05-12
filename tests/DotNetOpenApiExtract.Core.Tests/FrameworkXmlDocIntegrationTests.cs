using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Documentation;
using DotNetOpenApiExtract.Core.Loading;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests verifying that XML documentation from SDK ref packs is loaded
/// for framework types (e.g. <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c>) that end
/// up in <c>components/schemas</c> via <c>[ProducesResponseType]</c>.
///
/// This tests the fix for the bug where framework types had no description/property
/// descriptions in the generated spec, causing --validate to block CI with
/// schema.description / schema.property-description errors.
/// </summary>
public sealed class FrameworkXmlDocIntegrationTests : IDisposable
{
    private readonly AssemblyLoader _loader;

    public FrameworkXmlDocIntegrationTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
    }

    public void Dispose() => _loader.Dispose();

    // =========================================================================
    // AssemblyLoader.GetXmlDocumentationFiles — framework XML discovery
    // =========================================================================

    [Fact]
    public void GetXmlDocumentationFiles_ContainsAspNetCoreHttpAbstractionsXml()
    {
        // Microsoft.AspNetCore.Http.Abstractions.xml contains ProblemDetails docs.
        // It lives in the ASP.NET Core ref pack.
        var xmlFiles = _loader.GetXmlDocumentationFiles();

        var abstractionsXml = xmlFiles.FirstOrDefault(f =>
            Path.GetFileName(f).Equals(
                "Microsoft.AspNetCore.Http.Abstractions.xml",
                StringComparison.OrdinalIgnoreCase));

        abstractionsXml.Should().NotBeNull(
            because: "Microsoft.AspNetCore.Http.Abstractions.xml must be discoverable from the ASP.NET Core ref pack. " +
                     $"Discovered files: {string.Join(", ", xmlFiles.Take(10))}...");
    }

    // =========================================================================
    // XmlDocParser — reads ProblemDetails type and property docs from framework XML
    // =========================================================================

    [Fact]
    public void XmlDocParser_MultiSource_LoadsProblemDetailsTypeDoc()
    {
        // Build the same path list the builder would use
        var xmlFiles = _loader.GetXmlDocumentationFiles();
        var parser = XmlDocParser.FromSources(xmlFiles);

        // ProblemDetails lives in Microsoft.AspNetCore.Mvc namespace
        // Its type key: "T:Microsoft.AspNetCore.Mvc.ProblemDetails"
        // We use a fake Type proxy via the key, but XmlDocParser.GetTypeDoc takes a Type.
        // To avoid needing a real MetadataLoadContext type, directly probe via the internal
        // dictionary by exercising the key that AssemblyLoader resolves.
        //
        // Instead, we load the type from the AssemblyLoader's context and call GetTypeDoc.
        var problemDetailsType = _loader.FindType("Microsoft.AspNetCore.Mvc.ProblemDetails");
        problemDetailsType.Should().NotBeNull(
            because: "ProblemDetails must be resolvable from the assembly loader context");

        var typeDoc = parser.GetTypeDoc(problemDetailsType!);
        typeDoc.Should().NotBeNull(
            because: "ProblemDetails type doc must be found in the framework XML");
        typeDoc!.Summary.Should().NotBeNullOrWhiteSpace(
            because: "ProblemDetails has a non-empty <summary> in the framework XML");
        typeDoc.Summary.Should().Contain("machine-readable",
            because: "the framework XML summary for ProblemDetails mentions 'machine-readable'");
    }

    [Fact]
    public void XmlDocParser_MultiSource_LoadsProblemDetailsPropertyDocs()
    {
        var xmlFiles = _loader.GetXmlDocumentationFiles();
        var parser = XmlDocParser.FromSources(xmlFiles);

        var problemDetailsType = _loader.FindType("Microsoft.AspNetCore.Mvc.ProblemDetails");
        problemDetailsType.Should().NotBeNull();

        // Verify key property docs are available
        var properties = new[] { "Type", "Title", "Status", "Detail", "Instance" };
        foreach (var propName in properties)
        {
            var propDoc = parser.GetPropertyDoc(problemDetailsType!, propName);
            propDoc.Should().NotBeNull(
                because: $"ProblemDetails.{propName} has a <summary> in the framework XML");
            propDoc!.Summary.Should().NotBeNullOrWhiteSpace(
                because: $"ProblemDetails.{propName} <summary> must not be empty");
        }
    }

    [Fact]
    public void XmlDocParser_MultiSource_ProjectXmlWinsOverFrameworkXml()
    {
        // If project XML and framework XML both define the same key, project XML wins.
        // Since SampleApi.xml doesn't define ProblemDetails, the framework XML provides it.
        // This test verifies the order does NOT clobber the project XML entries.
        var xmlFiles = _loader.GetXmlDocumentationFiles();
        var parser = XmlDocParser.FromSources(xmlFiles);

        // UserDto is only in SampleApi.xml — must still be found
        var userDtoType = _loader.Assembly.GetType("SampleApi.Models.UserDto");
        userDtoType.Should().NotBeNull();

        var userDtoDoc = parser.GetTypeDoc(userDtoType!);
        userDtoDoc.Should().NotBeNull(
            because: "UserDto doc from project XML must survive multi-source merge");
        userDtoDoc!.Summary.Should().Be("User data transfer object");
    }

    // =========================================================================
    // End-to-end: full OpenApiDocumentBuilder pipeline with framework types
    // =========================================================================

    [Fact]
    public void Build_ProblemDetailsInSchema_HasDescriptionFromFrameworkXml()
    {
        // SampleApi now has FrameworkTypeRefController which declares:
        //   [ProducesResponseType(typeof(ProblemDetails), 422)]
        // This pulls ProblemDetails into components/schemas.
        // After the fix, its schema.description should come from the framework XML.
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Components.Should().NotBeNull();
        document.Components!.Schemas.Should().NotBeNull();

        // ProblemDetails schema must be present (pulled in by FrameworkTypeRefController)
        var hasProblemDetailsSchema = document.Components.Schemas.Keys
            .Any(k => k.Contains("ProblemDetails", StringComparison.OrdinalIgnoreCase));

        hasProblemDetailsSchema.Should().BeTrue(
            because: "FrameworkTypeRefController declares [ProducesResponseType(typeof(ProblemDetails), 422)] " +
                     $"so the schema must appear. Schemas: {string.Join(", ", document.Components.Schemas.Keys)}");

        // Find the actual schema key
        var problemDetailsKey = document.Components.Schemas.Keys
            .First(k => k.Contains("ProblemDetails", StringComparison.OrdinalIgnoreCase));
        var schema = document.Components.Schemas[problemDetailsKey] as OpenApiSchema;
        schema.Should().NotBeNull();

        // The schema description must be populated from the framework XML
        schema!.Description.Should().NotBeNullOrWhiteSpace(
            because: "ProblemDetails schema.description must be populated from the framework XML doc. " +
                     "If empty, the XML doc loading from SDK ref packs is not working.");
    }

    [Fact]
    public void Build_ProblemDetailsInSchema_PropertyDescriptionsFromFrameworkXml()
    {
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        var problemDetailsKey = document.Components?.Schemas?.Keys
            .FirstOrDefault(k => k.Contains("ProblemDetails", StringComparison.OrdinalIgnoreCase));

        problemDetailsKey.Should().NotBeNull(
            because: "FrameworkTypeRefController declares [ProducesResponseType(typeof(ProblemDetails), 422)]");

        var schemas = document.Components!.Schemas!;
        var schema = schemas[problemDetailsKey!] as OpenApiSchema;
        schema.Should().NotBeNull();

        // At least some properties should have descriptions
        var propertiesWithDescriptions = schema!.Properties?
            .Where(p => !string.IsNullOrWhiteSpace(p.Value?.Description))
            .Select(p => p.Key)
            .ToList() ?? [];

        propertiesWithDescriptions.Should().NotBeEmpty(
            because: "ProblemDetails properties must have descriptions from the framework XML. " +
                     "Properties found: " + string.Join(", ", schema.Properties?.Keys ?? []));
    }
}
