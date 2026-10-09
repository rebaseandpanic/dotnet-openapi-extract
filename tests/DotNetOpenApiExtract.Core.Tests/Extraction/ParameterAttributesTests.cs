using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>ModernApi for every version: parsed document and diagnostics.</summary>
public sealed class ParameterAttributesFixture
{
    public ParameterAttributesFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                OpenApiVersion = version,
                OnDiagnostic   = onDiagnostic,
            });
            Documents[version] = JsonNode.Parse(
                document.SerializeAsJsonAsync(version, CancellationToken.None).GetAwaiter().GetResult())!;
            Diagnostics[version] = diagnostics;
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, IReadOnlyList<ExtractionDiagnostic>> Diagnostics { get; } = [];
}

/// <summary>
/// Validation attributes on action parameters give the parameter schema the keywords they give a
/// DTO property; <c>[SwaggerParameter(Required)]</c> wins over the inferred value except on a path
/// parameter; <c>[SwaggerTag(description, externalDocsUrl)]</c> gives the tag its external docs.
/// </summary>
public class ParameterAttributesTests(ParameterAttributesFixture fixture) : IClassFixture<ParameterAttributesFixture>
{
    private static readonly string[] ConstraintKeys =
        ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "minLength", "maxLength", "minItems", "maxItems", "pattern", "format", "enum", "not"];

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    private JsonObject Operation(OpenApiSpecVersion version, string path) =>
        fixture.Documents[version]["paths"]![path]!["get"]!.AsObject();

    private static JsonObject Parameter(JsonObject operation, string name) =>
        operation["parameters"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!.AsObject();

    /// <summary>The constraint keywords of a parameter's schema: strings as they are, other values as JSON text.</summary>
    private static Dictionary<string, string> Constraints(JsonObject parameter) =>
        parameter["schema"]!.AsObject().Where(p => ConstraintKeys.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value!.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? p.Value.GetValue<string>()
                : p.Value.ToJsonString());

    public static TheoryData<OpenApiSpecVersion, string, string[]> ConstraintCases()
    {
        var data = new TheoryData<OpenApiSpecVersion, string, string[]>();
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var v30 = version == OpenApiSpecVersion.OpenApi3_0;
            data.Add(version, "code", ["minLength=2", "maxLength=8"]);
            data.Add(version, "level", v30 ? ["format=int32", "minimum=1", "exclusiveMinimum=true", "maximum=10"] : ["format=int32", "exclusiveMinimum=1", "maximum=10"]);
            data.Add(version, "ratio", v30 ? ["format=double", "minimum=0.5", "maximum=9.5", "exclusiveMaximum=true"] : ["format=double", "minimum=0.5", "exclusiveMaximum=9.5"]);
            data.Add(version, "slug", ["pattern=^[a-z]+$"]);
            data.Add(version, "tags", ["minItems=1", "maxItems=3"]);
            data.Add(version, "word", ["minLength=2", "maxLength=5"]);
            data.Add(version, "ids", ["minItems=1", "maxItems=4"]);
            data.Add(version, "mail", ["format=email"]);
            data.Add(version, "color", ["enum=[\"red\",\"green\"]"]);
            data.Add(version, "count", ["format=int32", "not={\"enum\":[0]}"]);
            data.Add(version, "clipped", ["minLength=1", "maxLength=4"]);
            data.Add(version, "pick", ["minItems=5", "maxItems=9"]);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ConstraintCases))]
    public void ValidationAttribute_OnAParameter_GivesTheKeywordsItGivesAProperty(OpenApiSpecVersion version, string name, string[] expected)
    {
        var operation = Operation(version, "/parameter-attributes/constraints/{code}");

        Constraints(Parameter(operation, name)).Select(c => $"{c.Key}={c.Value}").Should().BeEquivalentTo(expected);
        fixture.Diagnostics[version].Should().NotContain(d =>
            d.Location != null && d.Location.StartsWith("#/paths/~1parameter-attributes~1constraints~1{code}", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void DeclaredRequired_WinsOverTheInferredValue_ExceptOnAPath(OpenApiSpecVersion version)
    {
        var operation = Operation(version, "/parameter-attributes/required/{id}");

        Parameter(operation, "id")["required"]!.GetValue<bool>().Should().BeTrue(because: "OpenAPI requires every path parameter");
        Parameter(operation, "optionalByType")["required"]!.GetValue<bool>().Should().BeTrue();
        (Parameter(operation, "requiredByType")["required"]?.GetValue<bool>() ?? false).Should().BeFalse();
        Parameter(operation, "inferred")["required"]!.GetValue<bool>().Should().BeTrue();

        var index = operation["parameters"]!.AsArray().Select((p, i) => (p, i)).Single(x => x.p!["name"]!.GetValue<string>() == "id").i;
        var warning = fixture.Diagnostics[version]
            .Where(d => d.Location == $"#/paths/~1parameter-attributes~1required~1{{id}}/get/parameters/{index}")
            .Should().ContainSingle().Which;
        warning.Code.Should().Be(ExtractionDiagnosticCodes.ParameterPathRequiredKept);
        warning.Subjects.Should().Equal("id");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SwaggerTag_GivesTheControllerTagItsExternalDocs(OpenApiSpecVersion version)
    {
        var tag = fixture.Documents[version]["tags"]!.AsArray()
            .Single(t => t!["name"]!.GetValue<string>() == "ParameterAttributes")!.AsObject();

        tag["description"]!.GetValue<string>().Should().Be("Parameter attributes");
        tag["externalDocs"]!["url"]!.GetValue<string>().Should().Be("https://example.com/docs/parameters");
    }

    private JsonObject BodySchema(OpenApiSpecVersion version, string path, string mediaType = "application/json") =>
        fixture.Documents[version]["paths"]![path]!["post"]!["requestBody"]!["content"]![mediaType]!["schema"]!.AsObject();

    private static Dictionary<string, string> Keywords(JsonObject schema) =>
        schema.Where(p => ConstraintKeys.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value!.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? p.Value.GetValue<string>()
                : p.Value.ToJsonString());

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void ValidationAttributes_OnABodyParameter_ConstrainTheBody(OpenApiSpecVersion version)
    {
        Keywords(BodySchema(version, "/parameter-attributes/body-scalar")).Select(k => $"{k.Key}={k.Value}")
            .Should().BeEquivalentTo(["minLength=3", "maxLength=20"]);
        Keywords(BodySchema(version, "/parameter-attributes/body-collection")).Select(k => $"{k.Key}={k.Value}")
            .Should().BeEquivalentTo(["minItems=1", "maxItems=5"]);

        var wrapper = BodySchema(version, "/parameter-attributes/body-reference");
        wrapper["allOf"]![0]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/AnnotatedTarget");
        wrapper["format"]!.GetValue<string>().Should().Be("x-doc");
        fixture.Documents[version]["components"]!["schemas"]!["AnnotatedTarget"]!.AsObject().ContainsKey("format").Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void AllowedValues_OnAnEnumBody_UseTheNamesTheSerializerWrites(OpenApiSpecVersion version)
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        string Written(ModernApi.Models.Keywords.StjTint v) =>
            System.Text.Json.JsonSerializer.Deserialize<string>(System.Text.Json.JsonSerializer.Serialize(v, options))!;

        var schema = BodySchema(version, "/parameter-attributes/body-enum");
        var constraint = schema["allOf"]!.AsArray().Select(n => n!.AsObject()).Single(s => s["type"] == null && s["enum"] != null);
        constraint["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should()
            .Equal(Written(ModernApi.Models.Keywords.StjTint.Red), Written(ModernApi.Models.Keywords.StjTint.Crimson));
        Written(ModernApi.Models.Keywords.StjTint.Red).Should().Be("ruby", because: "the members are renamed on the wire");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void FormFields_GetTheirConstraints_AndRequiredFieldsAreListed(OpenApiSpecVersion version)
    {
        var form = BodySchema(version, "/parameter-attributes/form-constraints", "multipart/form-data");
        var properties = form["properties"]!.AsObject();
        Keywords(properties["title"]!.AsObject()).Select(k => $"{k.Key}={k.Value}").Should().BeEquivalentTo(["minLength=2", "maxLength=10"]);
        Keywords(properties["pages"]!.AsObject()).Select(k => $"{k.Key}={k.Value}").Should().BeEquivalentTo(["format=int32", "minimum=1", "maximum=5"]);
        Keywords(properties["tags"]!.AsObject()).Select(k => $"{k.Key}={k.Value}").Should().BeEquivalentTo(["maxItems=3"]);
        form["required"]!.AsArray().Select(n => n!.GetValue<string>()).Should().BeEquivalentTo(["title", "pages"],
            because: "[SwaggerParameter(Required = false)] makes note optional; nullable fields are optional");
    }
}
