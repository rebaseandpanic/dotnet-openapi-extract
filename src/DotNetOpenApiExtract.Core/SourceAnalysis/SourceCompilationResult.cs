using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Holds the result of compiling a set of C# source files via Roslyn.
/// This is a lightweight wrapper around <see cref="CSharpCompilation"/> that also
/// exposes the source root and the parsed syntax trees.
/// </summary>
public sealed class SourceCompilationResult
{
    /// <summary>The source root directory that was compiled.</summary>
    public string SourceRoot { get; }

    /// <summary>
    /// The Roslyn compilation built from all <c>.cs</c> files in <see cref="SourceRoot"/>.
    /// Note: the compilation intentionally omits most framework/ASP.NET Core references,
    /// so semantic resolution of external types will be partial.
    /// Use this for syntax-level analysis and literal extraction.
    /// </summary>
    public CSharpCompilation Compilation { get; }

    /// <summary>
    /// All <see cref="SyntaxTree"/> instances parsed from the source files,
    /// one per <c>.cs</c> file (excluding <c>bin/</c>, <c>obj/</c>, etc.).
    /// </summary>
    public IReadOnlyList<SyntaxTree> SyntaxTrees { get; }

    internal SourceCompilationResult(
        string sourceRoot,
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> syntaxTrees)
    {
        SourceRoot = sourceRoot;
        Compilation = compilation;
        SyntaxTrees = syntaxTrees;
    }
}
