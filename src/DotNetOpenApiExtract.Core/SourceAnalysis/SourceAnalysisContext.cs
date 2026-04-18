using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Carries the results of Roslyn source analysis for a single assembly.
/// Created during <c>OpenApiDocumentBuilder.Build</c> and made available to
/// future analysis passes within the same build operation.
/// </summary>
/// <remarks>
/// All properties are <see langword="null"/> when source analysis was skipped
/// (e.g. no source root found or Roslyn compilation failed). Consumer code must
/// null-check before use.
/// </remarks>
public sealed class SourceAnalysisContext
{
    /// <summary>
    /// The compiled Roslyn result, or <see langword="null"/> if compilation was not
    /// performed or failed.
    /// </summary>
    public SourceCompilationResult? CompilationResult { get; }

    /// <summary>
    /// The entry-point syntax node, or <see langword="null"/> if it could not be found.
    /// This is either a <see cref="MethodDeclarationSyntax"/> (conventional Main)
    /// or a <see cref="CompilationUnitSyntax"/> (top-level statements).
    /// </summary>
    public SyntaxNode? EntryPointNode { get; }

    /// <summary>
    /// Whether source analysis was successfully performed (i.e. compilation result is available).
    /// </summary>
    public bool IsAvailable => CompilationResult != null;

    private ILookup<string, InvocationExpressionSyntax>? _invocationIndex;

    /// <summary>
    /// Lazy-built lookup of all invocations under <see cref="EntryPointNode"/>,
    /// keyed by simple method name. Shared across extractors to avoid repeated
    /// tree traversal.
    /// </summary>
    public ILookup<string, InvocationExpressionSyntax> InvocationsByName
    {
        get
        {
            if (_invocationIndex != null) return _invocationIndex;
            if (EntryPointNode == null)
            {
                _invocationIndex = Enumerable.Empty<InvocationExpressionSyntax>()
                    .ToLookup(_ => string.Empty);
                return _invocationIndex;
            }

            _invocationIndex = EntryPointNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .ToLookup(
                    inv => InvocationMatcher.GetSimpleMethodName(inv.Expression) ?? string.Empty,
                    StringComparer.Ordinal);
            return _invocationIndex;
        }
    }

    /// <summary>
    /// Creates a context with successful analysis results.
    /// </summary>
    internal SourceAnalysisContext(
        SourceCompilationResult compilationResult,
        SyntaxNode? entryPointNode)
    {
        CompilationResult = compilationResult;
        EntryPointNode = entryPointNode;
    }

    /// <summary>
    /// Creates an empty context representing skipped or failed analysis.
    /// </summary>
    internal SourceAnalysisContext()
    {
        CompilationResult = null;
        EntryPointNode = null;
    }

    /// <summary>
    /// The empty/unavailable context singleton used when source analysis is not possible.
    /// </summary>
    internal static readonly SourceAnalysisContext Empty = new();
}
