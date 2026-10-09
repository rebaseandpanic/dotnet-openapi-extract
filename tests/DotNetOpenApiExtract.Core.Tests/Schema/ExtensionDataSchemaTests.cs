using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>ModernApi for every version, plus 3.1 with a global <c>Disallow</c> for unmapped members.</summary>
public sealed class ExtensionDataFixture
{
    public ExtensionDataFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            Documents[version] = VersionedDocumentHarness.BuildAndSerializeAsync(
                VersionedDocumentHarness.ModernApiOptions(version), DocumentFormat.Json, CancellationToken.None).GetAwaiter().GetResult();
            if (version != OpenApiSpecVersion.OpenApi3_0)
                Conformance[version] = SchemaConformance.For(Documents[version]);
        }

        using var source = new TempDirectory();
        File.WriteAllText(Path.Combine(source.Path, "Program.cs"), """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            XmlPath        = TestPaths.ModernApiXml,
            SourceRoot     = source.Path,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_1,
            OnDiagnostic   = onDiagnostic,
        });
        GlobalDisallow = VersionedDocumentHarness.SerializeAsync(document, OpenApiSpecVersion.OpenApi3_1, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
        GlobalDisallowDiagnostics = diagnostics;
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, SchemaConformance> Conformance { get; } = [];

    public JsonNode GlobalDisallow { get; }

    public IReadOnlyList<ExtractionDiagnostic> GlobalDisallowDiagnostics { get; }
}

/// <summary>
/// A valid <c>[JsonExtensionData]</c> property is not a property of the schema; it makes the object
/// open to any additional value. Shapes System.Text.Json rejects are extraction errors.
/// </summary>
public class ExtensionDataSchemaTests(ExtensionDataFixture fixture) : IClassFixture<ExtensionDataFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";

    private static JsonObject BodySchema(JsonNode document, string path)
    {
        var reference = document["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!
            .GetValue<string>();
        return document["components"]!["schemas"]![reference[SchemasPrefix.Length..]]!.AsObject();
    }

    public static TheoryData<OpenApiSpecVersion, string> OpenShapes
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                foreach (var path in new[]
                {
                    "/keywords/extension/object", "/keywords/extension/element", "/keywords/extension/dictionary",
                    "/keywords/extension/json-object", "/keywords/extension/derived", "/keywords/extension/generic",
                })
                    data.Add(version, path);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OpenShapes))]
    public void ValidExtensionData_IsNoPropertyAndOpensTheObject(OpenApiSpecVersion version, string path)
    {
        var schema = BodySchema(fixture.Documents[version], path);

        schema["properties"]!.AsObject().Select(p => p.Key).Should().NotContain("extra");
        (schema["required"]?.AsArray().Select(r => r!.GetValue<string>()) ?? []).Should().NotContain("extra");
        schema["additionalProperties"].Should().BeOfType<JsonObject>().Which.Should().BeEmpty(because: "any JSON value is allowed");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void InheritedExtensionData_IsKeptWithTheDeclaredProperties(OpenApiSpecVersion version)
    {
        BodySchema(fixture.Documents[version], "/keywords/extension/derived")["properties"]!.AsObject().Select(p => p.Key)
            .Should().BeEquivalentTo(["name", "level"]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void IgnoredExtensionData_IsNeitherAPropertyNorOpen(OpenApiSpecVersion version)
    {
        var schema = BodySchema(fixture.Documents[version], "/keywords/extension/ignored");

        schema["properties"]!.AsObject().Select(p => p.Key).Should().Equal("name");
        schema["additionalProperties"].Should().BeNull();
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    [Fact]
    public void GlobalDisallow_YieldsToExtensionData_WithoutWarning()
    {
        BodySchema(fixture.GlobalDisallow, "/keywords/extension/object")["additionalProperties"]
            .Should().BeOfType<JsonObject>().Which.Should().BeEmpty();
        fixture.GlobalDisallowDiagnostics.Should().NotContain(d => d.Message.Contains("Extension", StringComparison.Ordinal)
                                                                  || d.Message.Contains("Unmapped", StringComparison.Ordinal));
    }

    // ── Shapes System.Text.Json rejects ─────────────────────────────────────

    public sealed class StringValues
    {
        [JsonExtensionData] public Dictionary<string, string>? Extra { get; set; }
    }

    public sealed class IntegerKeys
    {
        [JsonExtensionData] public Dictionary<int, object>? Extra { get; set; }
    }

    public sealed class ReadOnlyBag
    {
        [JsonExtensionData] public IReadOnlyDictionary<string, object>? Extra { get; set; }
    }

    public sealed class TwoBags
    {
        [JsonExtensionData] public Dictionary<string, object>? First { get; set; }
        [JsonExtensionData] public Dictionary<string, object>? Second { get; set; }
    }

    public sealed class ConstructorBound(Dictionary<string, object>? extra)
    {
        [JsonExtensionData] public Dictionary<string, object>? Extra { get; } = extra;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class DisallowedBag
    {
        [JsonExtensionData] public Dictionary<string, object>? Extra { get; set; }
    }

    public static TheoryData<Type, string> RejectedShapes => new()
    {
        { typeof(StringValues), "Extra" },
        { typeof(IntegerKeys), "Extra" },
        { typeof(ReadOnlyBag), "Extra" },
        { typeof(TwoBags), "Second" },
        { typeof(ConstructorBound), "Extra" },
        { typeof(DisallowedBag), "Extra" },
    };

    [Theory]
    [MemberData(nameof(RejectedShapes))]
    public void ShapeSystemTextJsonRejects_IsAnExtractionError(Type type, string member)
    {
        // The reference: System.Text.Json itself rejects the contract.
        var stj = () => JsonSerializer.Deserialize("{\"a\":1}", type, StjWire.Mvc());
        stj.Should().Throw<InvalidOperationException>();

        var generate = () => new SchemaGenerator().GenerateSchema(type);
        var error = generate.Should().Throw<OpenApiExtractionException>().Which;
        error.TypeName.Should().Be(type.FullName);
        error.MemberName.Should().Be(member);
    }

    // ── SC-003 ──────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void ObjectWrittenWithExtraMembers_PassesTheSchema_AndAWrongDeclaredTypeFails(OpenApiSpecVersion version)
    {
        var bag = new ExtensionObjectBag { Name = "n", Extra = new Dictionary<string, object> { ["color"] = "red", ["size"] = 3 } };
        var wire = StjWire.Serialize(bag, StjWire.Mvc());
        wire!["color"].Should().NotBeNull(because: "STJ writes the extension members inline");

        var reference = fixture.Documents[version]["paths"]!["/keywords/extension/object"]!["get"]!["responses"]!["200"]!
            ["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>();
        var id = reference[SchemasPrefix.Length..];

        var result = fixture.Conformance[version].ValidateComponent(id, wire);
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));

        wire["name"] = 5;
        fixture.Conformance[version].ValidateComponent(id, wire).IsValid.Should().BeFalse(because: "name is a declared string");
    }

    public static TheoryData<OpenApiSpecVersion> SchemaVersions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];
}
