using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using ModernApi.Models.Streaming;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Streaming;

/// <summary>Builds ModernApi once per OpenAPI version, collecting diagnostics.</summary>
public sealed class ServerSentEventsFixture
{
    public ServerSentEventsFixture()
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
            var json = VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Builds[version] = new CollectedBuild(json, diagnostics);
            if (version != OpenApiSpecVersion.OpenApi3_0)
                Conformance[version] = SchemaConformance.For(json);
        }
    }

    public Dictionary<OpenApiSpecVersion, CollectedBuild> Builds { get; } = [];

    public Dictionary<OpenApiSpecVersion, SchemaConformance> Conformance { get; } = [];
}

/// <summary>
/// <c>ServerSentEventsResult&lt;T&gt;</c> is described as <c>text/event-stream</c> with the event schema of
/// OpenAPI 3.2 (§4.14.4) as its item schema, and the events the real result writes pass that schema.
/// </summary>
public class ServerSentEventsTests(ServerSentEventsFixture fixture) : IClassFixture<ServerSentEventsFixture>
{
    private const string ItemSchemaCode = ExtractionDiagnosticCodes.MediaTypeItemSchemaMovedToExtension;

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    public static TheoryData<OpenApiSpecVersion> SchemaVersions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    private static string ItemKey(OpenApiSpecVersion version) =>
        version == OpenApiSpecVersion.OpenApi3_2 ? "itemSchema" : "x-oai-itemSchema";

    /// <summary>The library writes 2020-12 keywords nested in x-oai-itemSchema as x-jsonschema-* for 3.0.</summary>
    private static string Keyword(OpenApiSpecVersion version, string keyword) =>
        version == OpenApiSpecVersion.OpenApi3_0 ? $"x-jsonschema-{keyword}" : keyword;

    private JsonObject EventStream(OpenApiSpecVersion version, string path)
    {
        var content = fixture.Builds[version].Document["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!.AsObject();
        content.Select(p => p.Key).Should().BeEquivalentTo(["text/event-stream"]);
        return content["text/event-stream"]!.AsObject();
    }

    private JsonObject EventSchema(OpenApiSpecVersion version, string path)
    {
        var media = EventStream(version, path);
        media.Select(p => p.Key).Should().BeEquivalentTo([ItemKey(version)], because: "an event stream has an item schema and no schema");
        return media[ItemKey(version)]!.AsObject();
    }

    // ── Row: the event schema ───────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void JsonItems_GetTheEventSchemaWithJsonData(OpenApiSpecVersion version)
    {
        var schema = EventSchema(version, "/sse/items");

        schema["type"]!.GetValue<string>().Should().Be("object");
        schema["required"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Equal("data");
        schema["properties"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["data", "event", "id", "retry"]);

        var data = schema["properties"]!["data"]!;
        data["type"]!.GetValue<string>().Should().Be("string");
        data[Keyword(version, "contentMediaType")]!.GetValue<string>().Should().Be("application/json");
        data[Keyword(version, "contentSchema")]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/StreamItem");

        schema["properties"]!["event"]!["type"]!.GetValue<string>().Should().Be("string");
        schema["properties"]!["id"]!["type"]!.GetValue<string>().Should().Be("string");
        schema["properties"]!["retry"]!["type"]!.GetValue<string>().Should().Be("integer");
        schema["properties"]!["retry"]!["minimum"]!.GetValue<int>().Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void EventStream_HasOneWarningPerMediaTypeBefore32(OpenApiSpecVersion version)
    {
        var diagnostics = fixture.Builds[version].Diagnostics;
        const string pointer = "#/paths/~1sse~1items/get/responses/200/content/text~1event-stream";

        var atMediaType = diagnostics.Where(d => d.Location == pointer).ToList();
        if (version == OpenApiSpecVersion.OpenApi3_2)
        {
            atMediaType.Should().BeEmpty();
        }
        else
        {
            atMediaType.Should().ContainSingle().Which.Code.Should().Be(ItemSchemaCode);
        }

        diagnostics.Should().NotContain(d => d.Location != null && d.Location.StartsWith(pointer + "/", StringComparison.Ordinal),
            because: "the media type warning covers contentMediaType and contentSchema inside");
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Components_HaveNoResultWrapper(OpenApiSpecVersion version)
    {
        fixture.Builds[version].Document["components"]!["schemas"]!.AsObject().Select(p => p.Key)
            .Should().NotContain(k => k.Contains("ServerSentEvents", StringComparison.Ordinal));
    }

    // ── Row: string and byte[] are written as the data itself ───────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void TextAndBytes_DataIsAPlainString(OpenApiSpecVersion version)
    {
        foreach (var path in new[] { "/sse/text", "/sse/bytes" })
        {
            var data = EventSchema(version, path)["properties"]!["data"]!.AsObject();
            data.Select(p => p.Key).Should().BeEquivalentTo(["type"], because: path);
            data["type"]!.GetValue<string>().Should().Be("string");
        }
    }

    // ── Row: recursive data ─────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void RecursiveData_IsFiniteAndEveryReferenceResolves(OpenApiSpecVersion version)
    {
        var document = fixture.Builds[version].Document;
        var data = EventSchema(version, "/sse/tree")["properties"]!["data"]!;
        data[Keyword(version, "contentSchema")]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/TreeEvent");

        var children = document["components"]!["schemas"]!["TreeEvent"]!["properties"]!["children"]!;
        (children["items"] ?? children["allOf"]![0]!["items"])!["$ref"]!.GetValue<string>()
            .Should().Be("#/components/schemas/TreeEvent");

        foreach (var reference in JsonReferences.All(document))
            JsonReferences.Resolve(document, reference).Should().NotBeNull(because: reference);
    }

    // ── Row: SC-003, events written by the real ServerSentEventsResult ──────

    private static async IAsyncEnumerable<T> Sequence<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static Task<SseResponse> WriteAsync<T>(IAsyncEnumerable<T> items, string? eventType = null) =>
        SseWire.ExecuteAsync(TypedResults.ServerSentEvents(items, eventType), null, TestContext.Current.CancellationToken);

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public async Task WrittenEvents_PassTheEventSchema_AndTheirDataPassesTheContentSchema(OpenApiSpecVersion version)
    {
        var conformance = fixture.Conformance[version];
        var schema = EventSchema(version, "/sse/items");
        var response = await WriteAsync(Sequence(
            new StreamItem { Sequence = 1, Text = "one" },
            new StreamItem { Sequence = 2, Text = "two" }), "item");

        response.ContentType.Should().StartWith("text/event-stream");
        response.Events.Should().HaveCount(2);
        foreach (var written in response.Events)
        {
            var asEvent = conformance.ValidateSchema(schema, written.ToEventObject());
            asEvent.IsValid.Should().BeTrue(because: string.Join("; ", asEvent.Errors));

            var asData = conformance.ValidateSchema(schema["properties"]!["data"]!["contentSchema"]!, written.ParseJsonData());
            asData.IsValid.Should().BeTrue(because: string.Join("; ", asData.Errors));
        }
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public async Task NullItem_IsEmptyDataThatPassesWithoutJsonParsing(OpenApiSpecVersion version)
    {
        var schema = EventSchema(version, "/sse/items");
        var response = await WriteAsync(Sequence<StreamItem?>([null]));

        var written = response.Events.Should().ContainSingle().Subject;
        written.IsEmptyData.Should().BeTrue();
        var result = fixture.Conformance[version].ValidateSchema(schema, written.ToEventObject());
        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void LiteralDataOfTheWrongJsonType_FailsTheContentSchema(OpenApiSpecVersion version)
    {
        var contentSchema = EventSchema(version, "/sse/items")["properties"]!["data"]!["contentSchema"]!;

        fixture.Conformance[version].ValidateSchema(contentSchema, JsonNode.Parse("\"one\"")).IsValid
            .Should().BeFalse(because: "a JSON string is not a StreamItem object");
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public async Task RawTextAndBytes_PassTheSchemaWithoutContentKeywords(OpenApiSpecVersion version)
    {
        var conformance = fixture.Conformance[version];

        var text = await WriteAsync(Sequence("first line", "{not json"));
        foreach (var written in text.Events)
            conformance.ValidateSchema(EventSchema(version, "/sse/text"), written.ToEventObject()).IsValid.Should().BeTrue();

        var bytes = await WriteAsync(Sequence<byte[]>([104, 105], [0x7B]));
        bytes.Events.Should().HaveCount(2);
        foreach (var written in bytes.Events)
            conformance.ValidateSchema(EventSchema(version, "/sse/bytes"), written.ToEventObject()).IsValid.Should().BeTrue();
    }
}
