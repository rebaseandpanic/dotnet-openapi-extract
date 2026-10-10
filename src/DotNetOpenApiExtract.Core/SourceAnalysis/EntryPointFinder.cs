using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Locates the entry-point syntax node within a Roslyn <see cref="CSharpCompilation"/>
/// based on the <see cref="MethodBase"/> returned by <see cref="System.Reflection.Assembly.EntryPoint"/>.
/// </summary>
/// <remarks>
/// Handles two entry-point styles:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Conventional <c>Program.Main(string[])</c></b> — a normal static method;
///       located via full type name + method name matching.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Top-level statements</b> — the compiler generates a synthetic
///       <c>Program.&lt;Main&gt;$</c> method; in source there is no explicit class.
///       The finder returns the <see cref="CompilationUnitSyntax"/> of the file that
///       contains top-level statements (i.e. whose members are not all namespace/type
///       declarations).
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class EntryPointFinder
{
    // The compiler-generated entry-point method name for top-level statements.
    // .NET 6+ compilers (Roslyn 4.x) generate "<Main>$"; earlier or differently-configured
    // compilers may generate "<Main>" (without the "$" suffix). Both indicate top-level statements.
    private const string SyntheticMainMethodName = "<Main>$";
    private const string SyntheticMainMethodNameLegacy = "<Main>";

    /// <summary>
    /// Finds the syntax node corresponding to the entry point of the application.
    /// </summary>
    /// <param name="entryPoint">
    /// The <see cref="MethodBase"/> returned by <c>Assembly.EntryPoint</c>.
    /// May be <see langword="null"/> for class libraries (no entry point).
    /// </param>
    /// <param name="compilation">
    /// The Roslyn compilation built from the project's source files.
    /// </param>
    /// <returns>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       A <see cref="MethodDeclarationSyntax"/> for conventional <c>Main</c> methods.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       A <see cref="CompilationUnitSyntax"/> for top-level statements.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see langword="null"/> if the entry point cannot be located in the compilation.
    ///     </description>
    ///   </item>
    /// </list>
    /// </returns>
    public static SyntaxNode? Find(MethodBase? entryPoint, CSharpCompilation compilation) =>
        FindAll(entryPoint, compilation).FirstOrDefault();

    /// <summary>
    /// Every syntax node of <paramref name="compilation"/> that can be the entry point: each file with
    /// top-level statements, or each <c>Main</c> of a type named as the declaring type of
    /// <paramref name="entryPoint"/>. A project compiles
    /// with one; several mean the compilation holds files the assembly was not built from, and the caller
    /// chooses among them.
    /// </summary>
    /// <param name="entryPoint">The <see cref="MethodBase"/> returned by <c>Assembly.EntryPoint</c>, or <see langword="null"/>.</param>
    /// <param name="compilation">The Roslyn compilation built from the project's source files.</param>
    /// <returns>The candidates in the order of the compilation's syntax trees; empty when there is none.</returns>
    public static IReadOnlyList<SyntaxNode> FindAll(MethodBase? entryPoint, CSharpCompilation compilation)
    {
        if (entryPoint == null)
            return [];

        // Top-level statements: compiler generates "<Main>$" (or "<Main>" in some configurations).
        if (entryPoint.Name == SyntheticMainMethodName ||
            entryPoint.Name == SyntheticMainMethodNameLegacy)
            return FindTopLevelStatements(compilation);

        // Conventional Main: find by declaring type name + method name.
        return FindConventionalMain(entryPoint, compilation);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Top-level statements
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="CompilationUnitSyntax"/>s that contain top-level statements: those whose
    /// <c>Members</c> include at least one <see cref="GlobalStatementSyntax"/>.
    /// </summary>
    private static List<SyntaxNode> FindTopLevelStatements(CSharpCompilation compilation) =>
        compilation.SyntaxTrees
            .Select(tree => tree.GetCompilationUnitRoot())
            .Where(root => root.Members.Any(m => m is GlobalStatementSyntax))
            .ToList<SyntaxNode>();

    // ──────────────────────────────────────────────────────────────────────────
    // Conventional Main
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="MethodDeclarationSyntax"/>s matching the declaring type name and method name of
    /// <paramref name="entryPoint"/>.
    /// </summary>
    private static List<SyntaxNode> FindConventionalMain(
        MethodBase entryPoint,
        CSharpCompilation compilation)
    {
        var declaringType = entryPoint.DeclaringType;
        if (declaringType == null)
            return [];

        return compilation.SyntaxTrees
            .SelectMany(tree => FindMethodsInUnit(tree.GetCompilationUnitRoot(), declaringType.Name, entryPoint.Name))
            .ToList<SyntaxNode>();
    }

    /// <summary>
    /// The methods of <paramref name="root"/> with the given containing type name and method name.
    /// </summary>
    private static IEnumerable<MethodDeclarationSyntax> FindMethodsInUnit(
        SyntaxNode root,
        string typeName,
        string methodName)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!string.Equals(typeDecl.Identifier.Text, typeName, StringComparison.Ordinal))
                continue;

            foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (string.Equals(method.Identifier.Text, methodName, StringComparison.Ordinal))
                    yield return method;
            }
        }
    }
}
