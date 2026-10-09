using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using ModernApi.Models.Streaming;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Streaming;

/// <summary>One build: the serialized document and every diagnostic it delivered.</summary>
public sealed record CollectedBuild(JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics);

/// <summary>Builds ModernApi and SampleApi for every version, plus ModernApi with the streaming paths excluded.</summary>
public sealed class SequentialMediaTypeFixture
{
    public SequentialMediaTypeFixture()
    {
        foreach (var version in VersionedDocumentHarness.Versions)
        {
            ModernApi[version] = Build(onDiagnostic => WithSubscriber(VersionedDocumentHarness.ModernApiOptions(version), onDiagnostic));
            SampleApi[version] = Build(onDiagnostic => WithSubscriber(VersionedDocumentHarness.SampleApiOptions(version), onDiagnostic));
        }

        ModernApiExcluded = Build(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath        = TestPaths.ModernApiDll,
            XmlPath             = TestPaths.ModernApiXml,
            OpenApiVersion      = OpenApiSpecVersion.OpenApi3_0,
            ExcludePathPrefixes = ["/sequential"],
            OnDiagnostic        = onDiagnostic,
        });
    }

    public Dictionary<OpenApiSpecVersion, CollectedBuild> ModernApi { get; } = [];

    public Dictionary<OpenApiSpecVersion, CollectedBuild> SampleApi { get; } = [];

    public CollectedBuild ModernApiExcluded { get; }

    private static OpenApiDocumentOptions WithSubscriber(OpenApiDocumentOptions options, Action<ExtractionDiagnostic> onDiagnostic) => new()
    {
        AssemblyPath   = options.AssemblyPath,
        XmlPath        = options.XmlPath,
        OpenApiVersion = options.OpenApiVersion,
        OnDiagnostic   = onDiagnostic,
    };

    private static CollectedBuild Build(Func<Action<ExtractionDiagnostic>, OpenApiDocumentOptions> createOptions)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(createOptions);
        var version = createOptions(_ => { }).OpenApiVersion;
        var json = VersionedDocumentHarness.SerializeAsync(document, version, DocumentFormat.Json, CancellationToken.None)
            .GetAwaiter().GetResult();
        return new CollectedBuild(json, diagnostics);
    }
}

/// <summary>
/// An asynchronous sequence is described per media type: sequential media types get
/// <c>itemSchema</c> (3.2) or <c>x-oai-itemSchema</c> with one warning (3.0/3.1), JSON gets an array,
/// <c>text/event-stream</c> gets no schema and a formatter warning; a degradation nested under a
/// moved operation is covered by the operation's warning.
/// </summary>
public class SequentialMediaTypeTests(SequentialMediaTypeFixture fixture) : IClassFixture<SequentialMediaTypeFixture>
{
    private const string StreamItemRef = "#/components/schemas/StreamItem";
    private const string ItemSchemaCode = ExtractionDiagnosticCodes.MediaTypeItemSchemaMovedToExtension;
    private const string EventStreamCode = ExtractionDiagnosticCodes.ResponseEventStreamWithoutFormatter;
    private const string MovedCode = ExtractionDiagnosticCodes.OperationMovedToAdditionalOperations;

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    public static TheoryData<OpenApiSpecVersion, string, string> SequentialCases
    {
        get
        {
            var data = new TheoryData<OpenApiSpecVersion, string, string>();
            foreach (var version in VersionedDocumentHarness.Versions)
            {
                data.Add(version, "/sequential/jsonl", "application/jsonl");
                data.Add(version, "/sequential/ndjson", "application/x-ndjson");
                data.Add(version, "/sequential/json-seq", "application/json-seq");
            }
            return data;
        }
    }

    private static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

    private static string MediaTypePointer(string path, string method, string code, string mediaType) =>
        $"#/paths/{Escape(path)}/{method}/responses/{code}/content/{Escape(mediaType)}";

    private static JsonObject Content(JsonNode document, string path, string code = "200") =>
        document["paths"]![path]!["get"]!["responses"]![code]!["content"]!.AsObject();

    private static IEnumerable<ExtractionDiagnostic> At(IEnumerable<ExtractionDiagnostic> diagnostics, string locationPrefix) =>
        diagnostics.Where(d => d.Location != null && d.Location.StartsWith(locationPrefix, StringComparison.Ordinal));

    // ── Row: sequential media types ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(SequentialCases))]
    public void SequentialMediaType_GetsTheItemSchemaOfItsVersion(OpenApiSpecVersion version, string path, string mediaType)
    {
        var build = fixture.ModernApi[version];
        var media = Content(build.Document, path)[mediaType]!.AsObject();

        var itemKey = version == OpenApiSpecVersion.OpenApi3_2 ? "itemSchema" : "x-oai-itemSchema";
        media.Select(p => p.Key).Should().BeEquivalentTo([itemKey], because: "the sequence has an item schema and no schema");
        media[itemKey]!["$ref"]!.GetValue<string>().Should().Be(StreamItemRef);

        var pointer = MediaTypePointer(path, "get", "200", mediaType);
        var warnings = build.Diagnostics.Where(d => d.Code == ItemSchemaCode && d.Location == pointer).ToList();
        if (version == OpenApiSpecVersion.OpenApi3_2)
        {
            warnings.Should().BeEmpty();
        }
        else
        {
            warnings.Should().ContainSingle();
            warnings[0].ExtensionName.Should().Be("x-oai-itemSchema");
            warnings[0].Action.Should().Be(DiagnosticAction.MovedToExtension);
            warnings[0].RequiredVersion.Should().Be(OpenApiSpecVersion.OpenApi3_2);
            warnings[0].TargetVersion.Should().Be(version);
        }

        build.Diagnostics.Where(d => d.Location != null && d.Location.StartsWith(pointer + "/", StringComparison.Ordinal))
            .Should().BeEmpty(because: "the media type warning covers every node inside it");
    }

    [Theory]
    [MemberData(nameof(SequentialCases))]
    public void OtherResponsesOfTheAction_AreWrittenAsUsual(OpenApiSpecVersion version, string path, string _)
    {
        var error = Content(fixture.ModernApi[version].Document, path, "422");

        error.Select(p => p.Key).Should().BeEquivalentTo(["application/json"]);
        error["application/json"]!["schema"]!["$ref"]!.GetValue<string>().Should().Be("#/components/schemas/StreamError");
    }

    // ── Row: each media type separately ─────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void MixedMediaTypes_JsonGetsAnArray_NdjsonGetsItems(OpenApiSpecVersion version)
    {
        var build = fixture.ModernApi[version];
        var content = Content(build.Document, "/sequential/mixed");

        var json = content["application/json"]!["schema"]!;
        json["type"]!.GetValue<string>().Should().Be("array");
        json["items"]!["$ref"]!.GetValue<string>().Should().Be(StreamItemRef);

        var itemKey = version == OpenApiSpecVersion.OpenApi3_2 ? "itemSchema" : "x-oai-itemSchema";
        content["application/x-ndjson"]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo([itemKey]);

        var warnings = At(build.Diagnostics, "#/paths/~1sequential~1mixed/").Select(d => (d.Code, d.Location)).ToList();
        if (version == OpenApiSpecVersion.OpenApi3_2)
            warnings.Should().BeEmpty();
        else
            warnings.Should().Equal((ItemSchemaCode, MediaTypePointer("/sequential/mixed", "get", "200", "application/x-ndjson")));
    }

    // ── Row: IAsyncEnumerable<T> + text/event-stream ────────────────────────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void SequenceDeclaredAsEventStream_HasNoSchemaAndOneFormatterWarning(OpenApiSpecVersion version)
    {
        var build = fixture.ModernApi[version];
        var media = Content(build.Document, "/sequential/event-stream")["text/event-stream"]!.AsObject();

        media.Should().BeEmpty(because: "standard MVC cannot write the sequence as server-sent events");

        var warnings = build.Diagnostics.Where(d => d.Location == "GET /sequential/event-stream").ToList();
        warnings.Should().ContainSingle();
        warnings[0].Code.Should().Be(EventStreamCode);
        warnings[0].Subjects.Should().Equal(typeof(StreamItem).FullName);
        At(build.Diagnostics, "#/paths/~1sequential~1event-stream/").Should().BeEmpty();
    }

    // ── Row: suppression under a moved operation ────────────────────────────

    [Fact]
    public void QueryWithSequentialResponse_In30_OnlyTheMovedOperationIsReported()
    {
        var build = fixture.ModernApi[OpenApiSpecVersion.OpenApi3_0];
        var operation = "#/paths/~1sequential~1query-ndjson/x-oai-additionalOperations/QUERY";

        At(build.Diagnostics, "#/paths/~1sequential~1query-ndjson/").Select(d => (d.Code, d.Location))
            .Should().Equal([(MovedCode, operation)],
                because: "the itemSchema degradation lies inside the moved operation, whose warning covers it");
    }

    [Fact]
    public void QueryWithSequenceAsEventStream_In30_ReportsTheMoveAndTheFormatter()
    {
        var build = fixture.ModernApi[OpenApiSpecVersion.OpenApi3_0];
        var operation = "#/paths/~1sequential~1query-event-stream/x-oai-additionalOperations/QUERY";

        At(build.Diagnostics, "#/paths/~1sequential~1query-event-stream/").Select(d => (d.Code, d.Location))
            .Should().BeEquivalentTo([(MovedCode, operation), (EventStreamCode, operation)],
                because: "a warning about the input is never suppressed by a degradation");
    }

    [Fact]
    public void QueryWithPolymorphicItems_In30_SuppressionDoesNotFollowTheReference()
    {
        var build = fixture.ModernApi[OpenApiSpecVersion.OpenApi3_0];

        At(build.Diagnostics, "#/paths/~1sequential~1query-parcels/").Select(d => d.Code)
            .Should().Equal([MovedCode]);
        build.Diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.PolymorphismDiscriminatorNotExpressible
                                     && d.Location == "#/components/schemas/Parcel")
            .Should().ContainSingle(because: "a component is a subtree of its own, reachable from the moved operation");
    }

    // ── Row: excluded paths ─────────────────────────────────────────────────

    [Fact]
    public void ExcludedStreamingPaths_ProduceNoRecords()
    {
        var diagnostics = fixture.ModernApiExcluded.Diagnostics;

        diagnostics.Should().NotContain(d => d.Code == EventStreamCode);
        diagnostics.Should().NotContain(d => d.Location != null && d.Location.Contains("sequential", StringComparison.Ordinal));
        diagnostics.Should().Contain(d => d.Code == ItemSchemaCode,
            because: "kept paths with item schemas (server-sent events) are still reported, so the filter is by path");
        diagnostics.Should().NotContain(d => d.Location == "#/components/schemas/Parcel");
    }

    // ── Row: SC-003, JSON Lines items written by System.Text.Json ───────────

    [Theory]
    [InlineData("/sequential/jsonl", "application/jsonl")]
    [InlineData("/sequential/ndjson", "application/x-ndjson")]
    public void EachLineWrittenByStj_PassesTheItemSchema(string path, string mediaType)
    {
        var document = fixture.ModernApi[OpenApiSpecVersion.OpenApi3_2].Document;
        var conformance = SchemaConformance.For(document);
        var itemSchema = Content(document, path)[mediaType]!["itemSchema"]!;

        StreamItem[] items = [new() { Sequence = 1, Text = "one" }, new() { Sequence = 2, Text = "two" }];
        foreach (var item in items)
        {
            // One line of the stream is the element serialized on its own.
            var line = StjWire.Serialize(item, StjWire.Mvc());
            var result = conformance.ValidateSchema(itemSchema, line);
            result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
        }

        conformance.ValidateSchema(itemSchema, JsonValue.Create("one")).IsValid
            .Should().BeFalse(because: "a line holding a JSON string is not a StreamItem");
    }

    // ── Row: unchanged — [Produces("text/event-stream")] without a body ─────

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void EventStreamWithoutBody_IsUnchanged(OpenApiSpecVersion version)
    {
        var build = fixture.SampleApi[version];
        var content = build.Document["paths"]!["/api/v1/events"]!["get"]!["responses"]!["200"]!["content"]!.AsObject();

        content.Select(p => p.Key).Should().BeEquivalentTo(["text/event-stream"]);
        content["text/event-stream"]!.AsObject().Should().BeEmpty();
        build.Diagnostics.Should().NotContain(d => d.Location != null
            && (d.Location.Contains("/api/v1/events", StringComparison.Ordinal)
                || d.Location.Contains("~1api~1v1~1events", StringComparison.Ordinal)));
    }
}
