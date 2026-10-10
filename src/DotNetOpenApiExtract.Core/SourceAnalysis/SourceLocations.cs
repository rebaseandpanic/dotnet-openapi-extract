using Microsoft.CodeAnalysis;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// The place of a syntax node in the analysed sources, as <c>file:line</c> for
/// <see cref="Diagnostics.ExtractionDiagnostic.SourceLocation"/>.
/// </summary>
internal static class SourceLocations
{
    /// <summary>
    /// <c>file:line</c> of the start of <paramref name="node"/>: the file relative to
    /// <paramref name="sourceRoot"/> when it lies under it (with <c>/</c> separators), otherwise as the
    /// syntax tree names it; the line 1-based. A tree without a path is named <c>&lt;source&gt;</c>.
    /// </summary>
    public static string Of(SyntaxNode node, string? sourceRoot)
    {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var path = node.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(path))
            return $"<source>:{line}";

        if (!string.IsNullOrEmpty(sourceRoot))
        {
            var relative = Path.GetRelativePath(sourceRoot, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                path = relative;
        }

        return $"{path.Replace('\\', '/')}:{line}";
    }

    /// <summary><see cref="Of(SyntaxNode, string?)"/> with the source root of <paramref name="context"/>.</summary>
    public static string Of(SyntaxNode node, SourceAnalysisContext context) =>
        Of(node, context.CompilationResult?.SourceRoot);
}
