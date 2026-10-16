using System.Globalization;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Every keyword the tool writes next to <c>$ref</c> in 3.1 and 3.2 survives writing and reading
/// back the way standalone <c>validate</c> reads a file, in JSON and YAML: after reading, the
/// reference's own field holds the written value, and the referenced component does not take the
/// keyword. The validation rules of a place read only those own fields, so a keyword that were
/// lost, or moved to the component, would change what <c>validate</c> reports for the file.
/// </summary>
public sealed class RefSiblingsRoundTripTests
{
    /// <summary>
    /// A sibling keyword: how it is set on a reference, and how its value is read from the reference's
    /// own fields and from a schema, as text (<c>null</c> when it is not set).
    /// </summary>
    private sealed record Keyword(
        Action<JsonSchemaReference> Set,
        Func<JsonSchemaReference, string?> OnReference,
        Func<OpenApiSchema, string?> OnSchema,
        string Written);

    private static readonly Dictionary<string, Keyword> Keywords = new()
    {
        ["description"] = new(r => r.Description = "own-description", r => r.Description, s => s.Description, "own-description"),
        ["title"] = new(r => r.Title = "own-title", r => r.Title, s => s.Title, "own-title"),
        ["default"] = new(r => r.Default = JsonValue.Create("own-default"), r => r.Default?.ToJsonString(), s => s.Default?.ToJsonString(), "\"own-default\""),
        ["examples"] = new(r => r.Examples = [JsonValue.Create("ex1"), JsonValue.Create(2)], r => Nodes(r.Examples), s => Nodes(s.Examples), "[\"ex1\",2]"),
        ["deprecated"] = new(r => r.Deprecated = true, r => Flag(r.Deprecated), s => Flag(s.Deprecated), "true"),
        ["readOnly"] = new(r => r.ReadOnly = true, r => Flag(r.ReadOnly), s => Flag(s.ReadOnly), "true"),
        ["writeOnly"] = new(r => r.WriteOnly = true, r => Flag(r.WriteOnly), s => Flag(s.WriteOnly), "true"),
        ["minLength"] = new(r => r.MinLength = 1, r => Number(r.MinLength), s => Number(s.MinLength), "1"),
        ["maxLength"] = new(r => r.MaxLength = 9, r => Number(r.MaxLength), s => Number(s.MaxLength), "9"),
        ["minItems"] = new(r => r.MinItems = 2, r => Number(r.MinItems), s => Number(s.MinItems), "2"),
        ["maxItems"] = new(r => r.MaxItems = 8, r => Number(r.MaxItems), s => Number(s.MaxItems), "8"),
        ["minProperties"] = new(r => r.MinProperties = 3, r => Number(r.MinProperties), s => Number(s.MinProperties), "3"),
        ["maxProperties"] = new(r => r.MaxProperties = 7, r => Number(r.MaxProperties), s => Number(s.MaxProperties), "7"),
        ["minimum"] = new(r => r.Minimum = "1.5", r => r.Minimum, s => s.Minimum, "1.5"),
        ["maximum"] = new(r => r.Maximum = "10", r => r.Maximum, s => s.Maximum, "10"),
        ["exclusiveMinimum"] = new(r => r.ExclusiveMinimum = "0", r => r.ExclusiveMinimum, s => s.ExclusiveMinimum, "0"),
        ["exclusiveMaximum"] = new(r => r.ExclusiveMaximum = "11", r => r.ExclusiveMaximum, s => s.ExclusiveMaximum, "11"),
        ["pattern"] = new(r => r.Pattern = "^own$", r => r.Pattern, s => s.Pattern, "^own$"),
        ["format"] = new(r => r.Format = "own-format", r => r.Format, s => s.Format, "own-format"),
        ["enum"] = new(r => r.Enum = [JsonValue.Create("a"), JsonValue.Create("b")], r => Nodes(r.Enum), s => Nodes(s.Enum), "[\"a\",\"b\"]"),
        ["enum with duplicates"] = new(r => r.Enum = [JsonValue.Create("a"), JsonValue.Create("a")], r => Nodes(r.Enum), s => Nodes(s.Enum), "[\"a\",\"a\"]"),
        ["const"] = new(r => r.Const = "own-const", r => r.Const, s => s.Const, "own-const"),
        ["not"] = new(r => r.Not = new OpenApiSchema { Enum = [JsonValue.Create("bad")] }, r => NotEnum(r.Not), s => NotEnum(s.Not), "[\"bad\"]"),
        ["x-own"] = new(
            r => r.Extensions = new Dictionary<string, IOpenApiExtension> { ["x-own"] = new JsonNodeExtension(JsonValue.Create("v")) },
            r => Extension(r.Extensions),
            s => Extension(s.Extensions),
            "\"v\""),
    };

    private static readonly OpenApiSpecVersion[] Versions = [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];
    private static readonly DocumentFormat[] Formats = [DocumentFormat.Json, DocumentFormat.Yaml];

    public static TheoryData<string, OpenApiSpecVersion, DocumentFormat> KeywordCases
    {
        get
        {
            var data = new TheoryData<string, OpenApiSpecVersion, DocumentFormat>();
            foreach (var keyword in Keywords.Keys)
            foreach (var version in Versions)
            foreach (var format in Formats)
                data.Add(keyword, version, format);
            return data;
        }
    }

    public static TheoryData<OpenApiSpecVersion, DocumentFormat> VersionsAndFormats
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, DocumentFormat>();
            foreach (var version in Versions)
            foreach (var format in Formats)
                data.Add(version, format);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(KeywordCases))]
    public async Task SiblingKeyword_ReadsBackIntoTheReferencesOwnField_AndNotIntoTheComponent(
        string name, OpenApiSpecVersion version, DocumentFormat format)
    {
        var keyword = Keywords[name];
        var place = new OpenApiSchemaReference("Target", null);
        keyword.Set(place.Reference);

        var (schemas, _) = await RoundTripAsync(version, format, ("place", place));
        var read = schemas.Holder["place"];

        new
        {
            Form = read.GetType().Name,
            Own = read is OpenApiSchemaReference reference ? keyword.OnReference(reference.Reference) : null,
            Component = keyword.OnSchema(schemas.Target),
        }.Should().BeEquivalentTo(
            new { Form = nameof(OpenApiSchemaReference), Own = (string?)keyword.Written, Component = (string?)null },
            because: $"'{name}' written next to $ref in {version} {format} is read back as the reference's own value");
    }

    /// <summary>
    /// An empty <c>enum</c> does not survive in the file: Microsoft.OpenApi does not write it at all,
    /// next to <c>$ref</c> or in an inline schema alike — a property of the library's writer, not of
    /// this tool's code. Read back, the reference and the inline schema of the same document both have
    /// no <c>enum</c>, and the component does not take one. So the negative side of
    /// <c>schema.enum-filled</c> for a reference with siblings is checked on the built model only.
    /// </summary>
    [Theory]
    [MemberData(nameof(VersionsAndFormats))]
    public async Task EmptyEnum_IsNotInTheFileReadBack_ForAReferenceAsForAnInlineSchema(OpenApiSpecVersion version, DocumentFormat format)
    {
        var reference = new OpenApiSchemaReference("Target", null);
        reference.Reference.Description = "own-description";
        reference.Reference.Enum = [];
        var inline = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [] };

        var (schemas, _) = await RoundTripAsync(version, format, ("reference", reference), ("inline", inline));

        new
        {
            Reference = schemas.Holder["reference"] is OpenApiSchemaReference read ? Nodes(read.Reference.Enum) : "not a reference",
            Inline = schemas.Holder["inline"] is OpenApiSchema readInline ? Nodes(readInline.Enum) : "not a schema",
            Component = Nodes(schemas.Target.Enum),
        }.Should().BeEquivalentTo(
            new { Reference = (string?)null, Inline = (string?)null, Component = (string?)null },
            because: $"the writer drops an empty enum in {version} {format} whatever the form of the schema");
    }

    private sealed record ReadSchemas(IDictionary<string, IOpenApiSchema> Holder, OpenApiSchema Target);

    /// <summary>Writes a document whose <c>Holder</c> has <paramref name="places"/>, and reads it back.</summary>
    private static async Task<(ReadSchemas Schemas, OpenApiSpecVersion? Version)> RoundTripAsync(
        OpenApiSpecVersion version, DocumentFormat format, params (string Name, IOpenApiSchema Schema)[] places)
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "1" },
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Holder"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = places.ToDictionary(p => p.Name, p => p.Schema),
                    },
                    ["Target"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema> { ["name"] = new OpenApiSchema { Type = JsonSchemaType.String } },
                    },
                },
            },
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        var text = format == DocumentFormat.Json
            ? await document.SerializeAsJsonAsync(version, cancellationToken)
            : await document.SerializeAsYamlAsync(version, cancellationToken);
        var (read, readVersion) = await ValidateReader.ReadAsync(text, format, cancellationToken);

        var schemas = read.Components?.Schemas
            ?? throw new InvalidOperationException("The document read back has no component schemas.");
        var holder = (schemas["Holder"] as OpenApiSchema)?.Properties
            ?? throw new InvalidOperationException("The holder read back has no properties.");
        var target = schemas["Target"] as OpenApiSchema
            ?? throw new InvalidOperationException("The component read back is not a schema.");
        return (new ReadSchemas(holder, target), readVersion);
    }

    private static string? Nodes(IList<JsonNode>? nodes) =>
        nodes is null ? null : "[" + string.Join(",", nodes.Select(n => n?.ToJsonString() ?? "null")) + "]";

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Flag(bool? value) => value == true ? "true" : null;

    private static string? NotEnum(IOpenApiSchema? not) => not is OpenApiSchema schema ? Nodes(schema.Enum) : null;

    private static string? Extension(IDictionary<string, IOpenApiExtension>? extensions) =>
        extensions is not null && extensions.TryGetValue("x-own", out var extension) && extension is JsonNodeExtension node
            ? node.Node.ToJsonString()
            : null;
}
