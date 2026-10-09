using DotNetOpenApiExtract.Core.Diagnostics;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>
/// Turns the ledger of one build into delivered diagnostics, after the document is final:
/// collect → keep only records whose anchor is still in the document → suppress degradations nested
/// under another degradation (the warning unit is the topmost lost or moved node) → compute each
/// location from the actual output → deduplicate and order deterministically → deliver. Nothing is
/// delivered before the filtering is complete, because a subscriber cannot take a delivered warning back.
/// </summary>
internal static class DownlevelPass
{
    public static void Run(OpenApiDocument document, LossLedger ledger, DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // Step 1: records written during the build plus model rules on the finished document.
        var entries = ledger.Entries.Concat(DownlevelRules.Evaluate(document, ledger.TargetVersion)).ToList();
        if (entries.Count == 0)
            return;

        var componentOrder = (document.Components?.Schemas?.Keys ?? Enumerable.Empty<string>())
            .Select((id, index) => (id, index))
            .ToDictionary(c => c.id, c => c.index, StringComparer.Ordinal);
        var reachable = SchemaReachability.FromDocument(document);

        Deliver(
            entries,
            ledger.TargetVersion,
            IndexOperations(document, ledger.TargetVersion),
            reachable.Where(componentOrder.ContainsKey).ToDictionary(id => id, id => componentOrder[id], StringComparer.Ordinal),
            diagnostics);
    }

    /// <summary>
    /// The same rule for direct schema generation: records anchored on components, kept only when
    /// the component is reachable from <paramref name="roots"/> (the schemas the caller asked for).
    /// </summary>
    public static void RunForSchemas(
        IEnumerable<PendingLoss> entries,
        OpenApiSpecVersion targetVersion,
        IReadOnlyDictionary<string, IOpenApiSchema> schemas,
        IEnumerable<IOpenApiSchema> roots,
        DiagnosticBag diagnostics)
    {
        var list = entries.ToList();
        if (list.Count == 0)
            return;

        var order = schemas.Keys.Select((id, index) => (id, index)).ToDictionary(c => c.id, c => c.index, StringComparer.Ordinal);
        var reachable = SchemaReachability.FromSchemas(roots, schemas)
            .Where(order.ContainsKey)
            .ToDictionary(id => id, id => order[id], StringComparer.Ordinal);

        Deliver(list, targetVersion, new Dictionary<OpenApiOperation, (int, string, string)>(), reachable, diagnostics);
    }

    /// <summary>
    /// Steps 3–8: drop records whose anchor is unreachable; drop every degradation whose node lies
    /// strictly inside the node of another degradation (by the output tree, never across a
    /// <c>$ref</c>; document-level records take no part); locate the rest in the output, order them
    /// (document, then operations, then components, each in document order; ties by location and
    /// code) and deliver; the bag deduplicates.
    /// </summary>
    private static void Deliver(
        IReadOnlyList<PendingLoss> entries,
        OpenApiSpecVersion targetVersion,
        Dictionary<OpenApiOperation, (int Order, string Location, string Pointer)> operations,
        Dictionary<string, int> components,
        DiagnosticBag diagnostics)
    {
        // Step 3: reachability — a record whose anchor is not in the finished output is dropped.
        var kept = new List<(PendingLoss Entry, Located Place)>();
        foreach (var entry in entries)
        {
            if (TryLocate(entry.Anchor, operations, components, out var place))
                kept.Add((entry, place));
        }

        // Steps 4–5: the topmost degradation covers its subtree.
        var degradationNodes = kept
            .Where(k => k.Entry.Class == LossClass.Degradation && k.Place.Segments is { Length: > 0 })
            .Select(k => k.Place.Segments!)
            .ToList();

        var located = new List<(int Rank, int Order, ExtractionDiagnostic Diagnostic)>();
        foreach (var (entry, place) in kept)
        {
            if (entry.Class == LossClass.Degradation
                && place.Segments is { Length: > 0 } segments
                && degradationNodes.Any(ancestor => IsStrictDescendant(segments, ancestor)))
                continue;

            var location = place.Location;
            located.Add((place.Rank, place.Order, new ExtractionDiagnostic
            {
                Code            = entry.Code,
                Message         = location is null
                    ? $"OpenAPI {TargetVersion.Describe(targetVersion)} target: {entry.Message}"
                    : $"OpenAPI {TargetVersion.Describe(targetVersion)} target: {location}: {entry.Message}",
                TargetVersion   = targetVersion,
                Feature         = entry.Feature,
                Location        = location,
                Action          = entry.Action,
                ExtensionName   = entry.ExtensionName,
                RequiredVersion = entry.RequiredVersion,
                Subjects        = entry.Subjects,
            }));
        }

        foreach (var (_, _, diagnostic) in located
                     .OrderBy(l => l.Rank)
                     .ThenBy(l => l.Order)
                     .ThenBy(l => l.Diagnostic.Location, StringComparer.Ordinal)
                     .ThenBy(l => l.Diagnostic.Code, StringComparer.Ordinal))
        {
            diagnostics.Report(diagnostic);
        }
    }

    /// <summary>
    /// Where a record lands: its ordering rank and order, the delivered location, and the unescaped
    /// segments of its node in the output tree (<see langword="null"/> for the document itself).
    /// </summary>
    private readonly record struct Located(int Rank, int Order, string? Location, string[]? Segments);

    /// <summary>Whether <paramref name="node"/> lies strictly inside <paramref name="ancestor"/> (whole segments).</summary>
    private static bool IsStrictDescendant(string[] node, string[] ancestor)
    {
        if (node.Length <= ancestor.Length)
            return false;

        for (var i = 0; i < ancestor.Length; i++)
        {
            if (!string.Equals(node[i], ancestor[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Every operation of the finished document with its location in the output: <c>METHOD /path</c>
    /// where the operation keeps a Path Item field (or 3.2 <c>additionalOperations</c>), the JSON
    /// pointer into <c>x-oai-additionalOperations</c> where the serializer moves it for 3.0/3.1.
    /// </summary>
    private static Dictionary<OpenApiOperation, (int Order, string Location, string Pointer)> IndexOperations(
        OpenApiDocument document, OpenApiSpecVersion version)
    {
        var index = new Dictionary<OpenApiOperation, (int, string, string)>(ReferenceEqualityComparer.Instance);
        var order = 0;
        foreach (var (path, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                continue;

            foreach (var (method, operation) in pathItem.Operations)
            {
                var pointer = Validation.JsonPointerHelper.ForOperation(path, method.Method, version);
                var location = OperationPlacement.SlotOf(method.Method, version) == OperationSlot.ExtensionAdditionalOperations
                    ? pointer
                    : $"{OperationPlacement.NormalizeMethod(method.Method)} {path}";
                index[operation] = (order++, location, pointer);
            }
        }

        return index;
    }

    private static bool TryLocate(
        LossAnchor anchor,
        Dictionary<OpenApiOperation, (int Order, string Location, string Pointer)> operations,
        Dictionary<string, int> components,
        out Located place)
    {
        switch (anchor)
        {
            case LossAnchor.Document:
                place = new Located(0, 0, null, null);
                return true;

            case LossAnchor.Operation { Target: var operation } when operations.TryGetValue(operation, out var found):
                place = new Located(1, found.Order, found.Location, Segments(found.Pointer));
                return true;

            case LossAnchor.RequestBodyContent { Target: var bodyOperation }
                when operations.TryGetValue(bodyOperation, out var bodyFound)
                     && bodyOperation.RequestBody?.Content is { Count: > 0 } content:
                var bodySegments = Segments(bodyFound.Pointer).Concat(["requestBody", "content", content.Keys.First()]).ToArray();
                place = new Located(1, bodyFound.Order, Pointer(bodySegments), bodySegments);
                return true;

            case LossAnchor.Component { Id: var id } when components.TryGetValue(id, out var componentOrder):
                var schemaPointer = Validation.JsonPointerHelper.ForSchema(id);
                place = new Located(2, componentOrder, schemaPointer, Segments(schemaPointer));
                return true;

            case LossAnchor.Node { Parent: var parent, Path: var path }
                when TryLocate(parent, operations, components, out var parentPlace):
                var segments = (parentPlace.Segments ?? []).Concat(path).ToArray();
                place = new Located(parentPlace.Rank, parentPlace.Order, Pointer(segments), segments);
                return true;

            default:
                place = default;
                return false;
        }
    }

    /// <summary>Unescaped segments of a pointer of the form <c>#/a/b~1c</c>.</summary>
    private static string[] Segments(string pointer) =>
        pointer.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))
            .ToArray();

    /// <summary>The JSON pointer of <paramref name="segments"/>: <c>#/</c> plus the escaped segments.</summary>
    private static string Pointer(IEnumerable<string> segments) =>
        "#/" + string.Join('/', segments.Select(Validation.JsonPointerHelper.EncodeSegment));
}
