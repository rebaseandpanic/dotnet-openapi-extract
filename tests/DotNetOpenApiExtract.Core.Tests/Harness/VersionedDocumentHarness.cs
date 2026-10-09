using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.YamlReader;
using SharpYaml.Serialization;

namespace DotNetOpenApiExtract.Core.Tests.Harness;

/// <summary>Text format a document is serialized to.</summary>
internal enum DocumentFormat
{
    Json,
    Yaml,
}

/// <summary>
/// Builds a document through the public <see cref="OpenApiDocumentBuilder.Build"/> and
/// serializes it into a chosen OpenAPI version and text format, returning the parsed
/// output as a <see cref="JsonNode"/> for addressed assertions.
/// </summary>
/// <remarks>
/// Building loads the fixture assembly, which is the expensive step: build once per test
/// class (a class fixture) and serialize the same document as many times as needed.
/// </remarks>
internal static class VersionedDocumentHarness
{
    /// <summary>The three OpenAPI versions the tool targets.</summary>
    public static readonly IReadOnlyList<OpenApiSpecVersion> Versions =
    [
        OpenApiSpecVersion.OpenApi3_0,
        OpenApiSpecVersion.OpenApi3_1,
        OpenApiSpecVersion.OpenApi3_2,
    ];

    /// <summary>Options for the ModernApi fixture with its XML documentation.</summary>
    public static OpenApiDocumentOptions ModernApiOptions() => new()
    {
        AssemblyPath = TestPaths.ModernApiDll,
        XmlPath      = TestPaths.ModernApiXml,
    };

    /// <summary>Options for the SampleApi fixture (the 3.0 regression baseline) with its XML documentation.</summary>
    public static OpenApiDocumentOptions SampleApiOptions() => new()
    {
        AssemblyPath = TestPaths.SampleApiDll,
        XmlPath      = TestPaths.SampleApiXml,
    };

    /// <summary>Builds the document through the public Core entry point.</summary>
    public static OpenApiDocument Build(OpenApiDocumentOptions options) =>
        OpenApiDocumentBuilder.Build(options);

    /// <summary>
    /// Serializes <paramref name="document"/> into <paramref name="version"/> and
    /// <paramref name="format"/>, then parses the text into a <see cref="JsonNode"/>.
    /// YAML output is parsed by the same YAML reader stack the CLI uses for <c>validate</c>.
    /// </summary>
    public static async Task<JsonNode> SerializeAsync(
        OpenApiDocument document,
        OpenApiSpecVersion version,
        DocumentFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (format == DocumentFormat.Json)
        {
            var json = await document.SerializeAsJsonAsync(version, cancellationToken);
            return JsonNode.Parse(json)
                ?? throw new InvalidOperationException("Serialized JSON document is empty.");
        }

        var yaml = await document.SerializeAsYamlAsync(version, cancellationToken);
        var stream = new YamlStream();
        using (var reader = new StringReader(yaml))
            stream.Load(reader);

        if (stream.Documents.Count != 1)
            throw new InvalidOperationException(
                $"Serialized YAML must contain exactly one document, found {stream.Documents.Count}.");

        return stream.Documents[0].ToJsonNode()
            ?? throw new InvalidOperationException("Serialized YAML document is empty.");
    }

    /// <summary>Builds the document and serializes it in one step.</summary>
    public static async Task<JsonNode> BuildAndSerializeAsync(
        OpenApiDocumentOptions options,
        OpenApiSpecVersion version,
        DocumentFormat format,
        CancellationToken cancellationToken) =>
        await SerializeAsync(Build(options), version, format, cancellationToken);
}
