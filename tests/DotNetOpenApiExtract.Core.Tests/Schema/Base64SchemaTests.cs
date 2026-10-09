using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi built for every version, and once without any version (the default).</summary>
public sealed class Base64SchemaFixture
{
    public Base64SchemaFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var diagnostics = new List<ExtractionDiagnostic>();
            var options = new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = diagnostics.Add,
            };
            Documents[version] = VersionedDocumentHarness.BuildAndSerializeAsync(options, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Diagnostics[version] = diagnostics;
        }

        var unversioned = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.ModernApiDll,
            XmlPath      = TestPaths.ModernApiXml,
        });
        DefaultDocument = VersionedDocumentHarness.SerializeAsync(unversioned, OpenApiSpecVersion.OpenApi3_0, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, List<ExtractionDiagnostic>> Diagnostics { get; } = [];

    public JsonNode DefaultDocument { get; }
}

/// <summary>
/// <c>byte[]</c> and <c>[Base64String] string</c> are base64 strings in the form of the target version:
/// <c>format: byte</c> for 3.0, <c>contentEncoding: base64</c> without <c>format</c> for 3.1+.
/// </summary>
public class Base64SchemaTests(Base64SchemaFixture fixture) : IClassFixture<Base64SchemaFixture>
{
    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private static JsonObject Properties(JsonNode document) =>
        document["components"]!["schemas"]!["Base64Payload"]!["properties"]!.AsObject();

    private static IReadOnlyList<string> Types(JsonNode schema) =>
        schema["type"] is JsonArray array ? array.Select(t => t!.GetValue<string>()).ToList() : [schema["type"]!.GetValue<string>()];

    public static TheoryData<OpenApiSpecVersion, string, bool> Cases
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, bool>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                data.Add(version, "data", false);
                data.Add(version, "optionalData", true);
                data.Add(version, "token", false);
                data.Add(version, "optionalToken", true);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Base64Property_HasTheFormOfItsVersion(OpenApiSpecVersion version, string property, bool nullable)
    {
        var schema = Properties(fixture.Documents[version])[property]!.AsObject();
        var keys = schema.Select(p => p.Key).ToList();

        if (version == OpenApiSpecVersion.OpenApi3_0)
        {
            Types(schema).Should().Equal("string");
            schema["format"]!.GetValue<string>().Should().Be("byte");
            keys.Should().NotContain(k => k.Contains("contentEncoding", StringComparison.Ordinal));
            (schema["nullable"]?.GetValue<bool>() ?? false).Should().Be(nullable);
        }
        else
        {
            Types(schema).Should().BeEquivalentTo(nullable ? ["string", "null"] : ["string"]);
            schema["contentEncoding"]!.GetValue<string>().Should().Be("base64");
            keys.Should().NotContain("format");
        }
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void PlainString_IsNotBase64_AndNothingIsReported(OpenApiSpecVersion version)
    {
        var plain = Properties(fixture.Documents[version])["plain"]!.AsObject();
        plain.Select(p => p.Key).Should().NotContain(["format", "contentEncoding"]);

        fixture.Diagnostics[version].Should().NotContain(d => d.Location != null && d.Location.Contains("Base64Payload"),
            because: "the form is chosen by version, without loss");
    }

    [Fact]
    public void BuildWithoutVersion_UsesThe30Form()
    {
        var data = Properties(fixture.DefaultDocument)["data"]!;
        data["format"]!.GetValue<string>().Should().Be("byte");
        data["contentEncoding"].Should().BeNull();
    }

    [Fact]
    public void SchemaGeneratorWithoutVersion_UsesThe30Form()
    {
        var generator = new SchemaGenerator();
        generator.GenerateSchema(typeof(Base64Payload));

        var properties = generator.Schemas["Base64Payload"].Properties!;
        var data = (OpenApiSchema)properties["data"];
        data.Format.Should().Be("byte");
        data.ContentEncoding.Should().BeNull();
        ((OpenApiSchema)properties["token"]).Format.Should().Be("byte");
    }

    [Fact]
    public void SchemaGeneratorFor31_UsesContentEncoding()
    {
        var generator = new SchemaGenerator(new SchemaOptions { OpenApiVersion = OpenApiSpecVersion.OpenApi3_1 });
        generator.GenerateSchema(typeof(Base64Payload));

        var properties = generator.Schemas["Base64Payload"].Properties!;
        foreach (var name in new[] { "data", "token" })
        {
            var schema = (OpenApiSchema)properties[name];
            schema.ContentEncoding.Should().Be("base64", because: name);
            schema.Format.Should().BeNull(because: name);
        }
    }
}
