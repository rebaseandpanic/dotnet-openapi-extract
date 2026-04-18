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
    public static SyntaxNode? Find(MethodBase? entryPoint, CSharpCompilation compilation)
    {
        if (entryPoint == null)
            return null;

        // Top-level statements: compiler generates "<Main>$" (or "<Main>" in some configurations).
        // Both names indicate that the program uses top-level statements — return the
        // CompilationUnitSyntax that contains global statements.
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
    /// Finds the <see cref="CompilationUnitSyntax"/> that contains top-level statements.
    /// A compilation unit has top-level statements when its <c>Members</c> list includes
    /// at least one <see cref="GlobalStatementSyntax"/> (i.e. a member that is not a
    /// namespace or type declaration).
    /// </summary>
    private static SyntaxNode? FindTopLevelStatements(CSharpCompilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetCompilationUnitRoot();
            if (root.Members.Any(m => m is GlobalStatementSyntax))
                return root;
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Conventional Main
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds a <see cref="MethodDeclarationSyntax"/> matching the declaring type and
    /// method name of <paramref name="entryPoint"/>.
    /// </summary>
    private static SyntaxNode? FindConventionalMain(
        MethodBase entryPoint,
        CSharpCompilation compilation)
    {
        var declaringType = entryPoint.DeclaringType;
        if (declaringType == null)
            return null;

        var simpleTypeName = declaringType.Name;
        var methodName = entryPoint.Name;

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetCompilationUnitRoot();
            var found = FindMethodInUnit(root, simpleTypeName, methodName);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Recursively searches <paramref name="root"/> for a method with the given
    /// containing type name and method name.
    /// </summary>
    private static MethodDeclarationSyntax? FindMethodInUnit(
        SyntaxNode root,
        string typeName,
        string methodName)
    {
        // Search all type declarations in the tree
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!string.Equals(typeDecl.Identifier.Text, typeName, StringComparison.Ordinal))
                continue;

            foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                if (string.Equals(method.Identifier.Text, methodName, StringComparison.Ordinal))
                    return method;
            }
        }

        return null;
    }
}
