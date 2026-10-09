using DotNetOpenApiExtract.Core.Diagnostics;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>
/// Turns the ledger of one build into delivered diagnostics, after the document is final:
/// collect → keep only records whose anchor is still in the document → compute each location from
/// the actual output → deduplicate and order deterministically → deliver. Nothing is delivered
/// before the filtering is complete, because a subscriber cannot take a delivered warning back.
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

        Deliver(list, targetVersion, new Dictionary<OpenApiOperation, (int, string)>(), reachable, diagnostics);
    }

    /// <summary>
    /// Steps 3 and 6–8: drop records whose anchor is unreachable, locate the rest in the output,
    /// order them (document, then operations, then components, each in document order; ties by
    /// location and code) and deliver; the bag deduplicates.
    /// </summary>
    private static void Deliver(
        IReadOnlyList<PendingLoss> entries,
        OpenApiSpecVersion targetVersion,
        Dictionary<OpenApiOperation, (int Order, string Location)> operations,
        Dictionary<string, int> components,
        DiagnosticBag diagnostics)
    {
        var located = new List<(int Rank, int Order, ExtractionDiagnostic Diagnostic)>();
        foreach (var entry in entries)
        {
            // Reachability: a record whose anchor is not in the finished output is dropped.
            if (!TryLocate(entry.Anchor, operations, components, out var rank, out var order, out var location))
                continue;

            located.Add((rank, order, new ExtractionDiagnostic
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
    /// Every operation of the finished document with its location in the output: <c>METHOD /path</c>
    /// where the operation keeps a Path Item field (or 3.2 <c>additionalOperations</c>), the JSON
    /// pointer into <c>x-oai-additionalOperations</c> where the serializer moves it for 3.0/3.1.
    /// </summary>
    private static Dictionary<OpenApiOperation, (int Order, string Location)> IndexOperations(
        OpenApiDocument document, OpenApiSpecVersion version)
    {
        var index = new Dictionary<OpenApiOperation, (int, string)>(ReferenceEqualityComparer.Instance);
        var order = 0;
        foreach (var (path, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                continue;

            foreach (var (method, operation) in pathItem.Operations)
            {
                var location = OperationPlacement.SlotOf(method.Method, version) == OperationSlot.ExtensionAdditionalOperations
                    ? Validation.JsonPointerHelper.ForOperation(path, method.Method, version)
                    : $"{OperationPlacement.NormalizeMethod(method.Method)} {path}";
                index[operation] = (order++, location);
            }
        }

        return index;
    }

    private static bool TryLocate(
        LossAnchor anchor,
        Dictionary<OpenApiOperation, (int Order, string Location)> operations,
        Dictionary<string, int> components,
        out int rank,
        out int order,
        out string? location)
    {
        switch (anchor)
        {
            case LossAnchor.Document:
                (rank, order, location) = (0, 0, null);
                return true;

            case LossAnchor.Operation { Target: var operation } when operations.TryGetValue(operation, out var found):
                (rank, order, location) = (1, found.Order, found.Location);
                return true;

            case LossAnchor.Component { Id: var id } when components.TryGetValue(id, out var componentOrder):
                (rank, order, location) = (2, componentOrder, Validation.JsonPointerHelper.ForSchema(id));
                return true;

            default:
                (rank, order, location) = (0, 0, null);
                return false;
        }
    }
}
