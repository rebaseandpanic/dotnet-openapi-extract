using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.SourceAnalysis;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Extracts the path base configured via <c>app.UsePathBase("/prefix")</c>
/// from the Roslyn source analysis context.
/// </summary>
public static class PathBaseExtractor
{
    /// <summary>
    /// Scans the Roslyn source analysis context for calls to <c>app.UsePathBase("/prefix")</c>
    /// and returns the literal path argument, or <see langword="null"/> if not found or
    /// arguments are not literal strings.
    /// </summary>
    /// <param name="context">
    /// The source analysis context produced by Roslyn compilation of the entry-point source.
    /// When <see cref="SourceAnalysisContext.IsAvailable"/> is <see langword="false"/>
    /// or <see cref="SourceAnalysisContext.EntryPointNode"/> is <see langword="null"/>,
    /// the method returns <see langword="null"/> immediately.
    /// </param>
    /// <returns>
    /// A normalised path base string (leading <c>/</c>, no trailing <c>/</c>), or
    /// <see langword="null"/> when no unambiguous literal <c>UsePathBase</c> call is found.
    /// </returns>
    public static string? ExtractPathBase(SourceAnalysisContext context)
        => ExtractPathBase(context, onDiagnostic: null);

    /// <summary>
    /// Same as <see cref="ExtractPathBase(SourceAnalysisContext)"/>, but delivers warnings to
    /// <paramref name="onDiagnostic"/> instead of printing them to <c>Console.Error</c>.
    /// </summary>
    /// <param name="context">The source analysis context of the entry point.</param>
    /// <param name="onDiagnostic">
    /// Receives each warning. When <see langword="null"/>, warnings are printed to
    /// <c>Console.Error</c>, as <see cref="ExtractPathBase(SourceAnalysisContext)"/> does.
    /// </param>
    public static string? ExtractPathBase(
        SourceAnalysisContext context, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        if (!context.IsAvailable || context.EntryPointNode is null)
            return null;

        var invocations = InvocationMatcher
            .FindInvocations(context, "UsePathBase")
            .ToList();

        if (invocations.Count == 0)
            return null;

        if (invocations.Count > 1)
        {
            DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
            {
                Code    = ExtractionDiagnosticCodes.PathBaseMultipleCalls,
                Message = $"Found {invocations.Count} UsePathBase() calls in the entry point. Using the first one.",
            });
        }

        foreach (var invocation in invocations)
        {
            var raw = InvocationMatcher.GetLiteralStringArgument(invocation, 0);

            if (raw is null)
            {
                // Non-literal argument (variable, config access, etc.) — cannot resolve statically.
                DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
                {
                    Code    = ExtractionDiagnosticCodes.PathBaseNonLiteral,
                    Message = "UsePathBase() argument is not a string literal. Path base will not be emitted.",
                });
                return null;
            }

            return NormalizePathBase(raw);
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises a raw path base value:
    /// <list type="bullet">
    ///   <item>Empty string or <c>"/"</c> → <see langword="null"/> (no-op path base).</item>
    ///   <item>Ensures a leading <c>/</c>.</item>
    ///   <item>Removes any trailing <c>/</c>.</item>
    /// </list>
    /// </summary>
    internal static string? NormalizePathBase(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        // Ensure leading slash
        if (!raw.StartsWith('/'))
            raw = "/" + raw;

        // Trim trailing slash(es)
        raw = raw.TrimEnd('/');

        // After normalisation "/" becomes "" — treat as no-op
        if (raw.Length == 0 || raw == "/")
            return null;

        return raw;
    }
}
