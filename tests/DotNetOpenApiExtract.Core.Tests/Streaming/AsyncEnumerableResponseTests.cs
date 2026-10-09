using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Streaming;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Streaming;

/// <summary>Builds ModernApi once per OpenAPI version (JSON output).</summary>
public sealed class AsyncEnumerableResponseFixture
{
    public AsyncEnumerableResponseFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = VersionedDocumentHarness.BuildAndSerializeAsync(
                    VersionedDocumentHarness.ModernApiOptions(version), DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Documents[version] = document;
            if (version != OpenApiSpecVersion.OpenApi3_0)
                Conformance[version] = SchemaConformance.For(document);
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, SchemaConformance> Conformance { get; } = [];
}

/// <summary>
/// An asynchronous sequence returned by a controller is written by MVC as a JSON array, so its
/// response schema is an array of the element type, with no component for the sequence type.
/// </summary>
public class AsyncEnumerableResponseTests(AsyncEnumerableResponseFixture fixture) : IClassFixture<AsyncEnumerableResponseFixture>
{
    private const string StreamItemRef = "#/components/schemas/StreamItem";

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    public static TheoryData<OpenApiSpecVersion> SchemaVersions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    public static TheoryData<OpenApiSpecVersion, string> SequencePaths
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                foreach (var path in new[]
                {
                    "/streaming/plain", "/streaming/task", "/streaming/value-task",
                    "/streaming/action-result", "/streaming/custom", "/streaming/declared",
                })
                    data.Add(version, path);
            }
            return data;
        }
    }

    private JsonObject Content(OpenApiSpecVersion version, string path) =>
        fixture.Documents[version]["paths"]![path]!["get"]!["responses"]!["200"]!["content"]!.AsObject();

    private JsonNode Schema(OpenApiSpecVersion version, string path) =>
        Content(version, path)["application/json"]!["schema"]!;

    [Theory]
    [MemberData(nameof(SequencePaths))]
    public void Sequence_OnJson_IsAnArrayOfTheElement(OpenApiSpecVersion version, string path)
    {
        var schema = Schema(version, path);

        schema["type"]!.GetValue<string>().Should().Be("array");
        schema["items"]!["$ref"]!.GetValue<string>().Should().Be(StreamItemRef);
        schema.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["type", "items"]);
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void Components_HaveNoSequenceWrapper(OpenApiSpecVersion version)
    {
        var keys = fixture.Documents[version]["components"]!["schemas"]!.AsObject().Select(p => p.Key).ToList();

        keys.Should().Contain("StreamItem");
        keys.Should().NotContain(k => k.Contains("IAsyncEnumerable", StringComparison.Ordinal));
        keys.Should().NotContain(k => k.Contains("StreamItemSequence", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SseItemSequence_FromController_IsAJsonArrayOfSseItemObjects(OpenApiSpecVersion version)
    {
        var content = Content(version, "/streaming/sse-items");
        content.Select(p => p.Key).Should().BeEquivalentTo(["application/json"],
            because: "a controller writes the sequence with its JSON formatter; SSE is never guessed from the type name");

        var schema = content["application/json"]!["schema"]!;
        schema["type"]!.GetValue<string>().Should().Be("array");
        var reference = schema["items"]!["$ref"]!.GetValue<string>();
        var item = fixture.Documents[version]["components"]!["schemas"]![reference.Split('/')[^1]]!;
        item["type"]!.GetValue<string>().Should().Be("object");
        item["properties"]!.AsObject().Select(p => p.Key).Should().Contain(["data", "eventType"]);
        // The property carries a description, so the reference sits in an allOf wrapper.
        var data = item["properties"]!["data"]!;
        (data["$ref"] ?? data["allOf"]![0]!["$ref"])!.GetValue<string>().Should().Be(StreamItemRef);
    }

    [Fact]
    public void NullableElements_AreNullableItems_In30()
    {
        var items = Schema(OpenApiSpecVersion.OpenApi3_0, "/streaming/nullable-ints")["items"]!;

        items["type"]!.GetValue<string>().Should().Be("integer");
        items["nullable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void NullableElements_AreNullableItems_In31AndLater(OpenApiSpecVersion version)
    {
        var items = Schema(version, "/streaming/nullable-ints")["items"]!;

        items["type"]!.AsArray().Select(t => t!.GetValue<string>()).Should().BeEquivalentTo(["integer", "null"]);
        items["nullable"].Should().BeNull();
    }

    // ── SC-003: what System.Text.Json writes for the sequence passes the response schema ──

    private static async Task<JsonNode?> WriteAsync<T>(IAsyncEnumerable<T> sequence)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, sequence, StjWire.Mvc(), TestContext.Current.CancellationToken);
        return JsonNode.Parse(stream.ToArray());
    }

    private static async IAsyncEnumerable<T> Sequence<T>(params T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    private static readonly StreamItem[] Items =
    [
        new() { Sequence = 1, Text = "one" },
        new() { Sequence = 2, Text = "two" },
    ];

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public async Task WrittenSequence_PassesTheResponseSchema(OpenApiSpecVersion version)
    {
        var wire = await WriteAsync(new StreamItemSequence(Items));
        wire.Should().BeOfType<JsonArray>().Which.Should().HaveCount(2);

        foreach (var path in new[] { "/streaming/plain", "/streaming/custom" })
        {
            var result = fixture.Conformance[version].ValidateSchema(Schema(version, path), wire);
            result.IsValid.Should().BeTrue(because: $"{path}: {string.Join("; ", result.Errors)}");
        }
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public async Task WrittenSequenceWithNullElement_PassesTheNullableSchema(OpenApiSpecVersion version)
    {
        var wire = await WriteAsync(Sequence<int?>(1, null, 3));
        wire!.AsArray().Should().Contain(n => n == null);

        var result = fixture.Conformance[version].ValidateSchema(Schema(version, "/streaming/nullable-ints"), wire);

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public async Task WrittenSseItems_PassTheResponseSchema(OpenApiSpecVersion version)
    {
        var wire = await WriteAsync(Sequence(new SseItem<StreamItem>(Items[0], "created") { EventId = "1" }));

        var result = fixture.Conformance[version].ValidateSchema(Schema(version, "/streaming/sse-items"), wire);

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(SchemaVersions))]
    public void SingleObjectInsteadOfArray_FailsTheResponseSchema(OpenApiSpecVersion version)
    {
        var wire = StjWire.Serialize(Items[0], StjWire.Mvc());

        var result = fixture.Conformance[version].ValidateSchema(Schema(version, "/streaming/plain"), wire);

        result.IsValid.Should().BeFalse(because: "MVC writes the sequence as an array, never as one object");
    }
}
