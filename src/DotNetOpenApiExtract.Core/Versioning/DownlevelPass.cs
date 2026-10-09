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

        if (ledger.Entries.Count == 0)
            return;

        var operations = IndexOperations(document);

        var located = new List<(int Rank, int Order, ExtractionDiagnostic Diagnostic)>();
        foreach (var entry in ledger.Entries)
        {
            // Reachability: a record whose anchor is not in the finished document is dropped.
            if (!TryLocate(entry.Anchor, operations, out var rank, out var order, out var location))
                continue;

            located.Add((rank, order, new ExtractionDiagnostic
            {
                Code            = entry.Code,
                Message         = location is null
                    ? $"OpenAPI {TargetVersion.Describe(ledger.TargetVersion)} target: {entry.Message}"
                    : $"OpenAPI {TargetVersion.Describe(ledger.TargetVersion)} target: {location}: {entry.Message}",
                TargetVersion   = ledger.TargetVersion,
                Feature         = entry.Feature,
                Location        = location,
                Action          = entry.Action,
                ExtensionName   = entry.ExtensionName,
                RequiredVersion = entry.RequiredVersion,
                Subjects        = entry.Subjects,
            }));
        }

        // Document order: document-level records first, then operations in the order the paths
        // and methods are written; ties by location and code. The bag deduplicates.
        foreach (var (_, _, diagnostic) in located
                     .OrderBy(l => l.Rank)
                     .ThenBy(l => l.Order)
                     .ThenBy(l => l.Diagnostic.Location, StringComparer.Ordinal)
                     .ThenBy(l => l.Diagnostic.Code, StringComparer.Ordinal))
        {
            diagnostics.Report(diagnostic);
        }
    }

    private static Dictionary<OpenApiOperation, (int Order, string Location)> IndexOperations(OpenApiDocument document)
    {
        var index = new Dictionary<OpenApiOperation, (int, string)>(ReferenceEqualityComparer.Instance);
        var order = 0;
        foreach (var (path, pathItemInterface) in document.Paths)
        {
            if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                continue;

            foreach (var (method, operation) in pathItem.Operations)
                index[operation] = (order++, $"{method.Method.ToUpperInvariant()} {path}");
        }

        return index;
    }

    private static bool TryLocate(
        LossAnchor anchor,
        Dictionary<OpenApiOperation, (int Order, string Location)> operations,
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

            default:
                (rank, order, location) = (0, 0, null);
                return false;
        }
    }
}
