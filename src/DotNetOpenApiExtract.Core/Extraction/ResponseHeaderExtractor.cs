using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using static DotNetOpenApiExtract.Core.SourceAnalysis.TypeSyntaxHelper;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Scans a Roslyn <see cref="SourceAnalysisContext"/> for calls to
/// <c>context.Response.Headers.Append/Add/TryAdd("Name", ...)</c> inside
/// middleware registrations and middleware class bodies, and returns the
/// literal header names found.
/// </summary>
/// <remarks>
/// Two middleware shapes are recognised:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Inline lambda</b>: <c>app.Use(async (context, next) => { context.Response.Headers.Append("X-Foo", ...); })</c>
///       — the lambda body is scanned for header-mutation calls.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Class-based</b>: <c>app.UseMiddleware&lt;SomeMiddleware&gt;()</c>
///       — the middleware class is located by name across all syntax trees in the
///       compilation and its <c>InvokeAsync</c>/<c>Invoke</c> method body is scanned.
///     </description>
///   </item>
/// </list>
/// All matching is purely syntactic. Non-literal header names (variables,
/// configuration lookups, etc.) are skipped with a warning written to
/// <c>stderr</c>.
/// </remarks>
public static class ResponseHeaderExtractor
{
    private static readonly HashSet<string> HeaderMutationMethods =
        new(StringComparer.Ordinal) { "Append", "Add", "TryAdd" };

    // ──────────────────────────────────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans the Roslyn context for calls to
    /// <c>context.Response.Headers.Append/Add/TryAdd("Name", ...)</c>
    /// inside middleware registrations (<c>app.Use(...)</c>,
    /// <c>app.UseMiddleware&lt;T&gt;()</c>) and middleware class
    /// <c>InvokeAsync</c>/<c>Invoke</c> bodies. Returns the literal header
    /// names found, deduplicated and preserving first-occurrence order.
    /// Returns an empty list when <paramref name="context"/> is unavailable.
    /// </summary>
    /// <param name="context">The source analysis context to scan.</param>
    /// <returns>
    /// Deduplicated list of literal header names found in middleware bodies.
    /// </returns>
    public static IReadOnlyList<string> Extract(SourceAnalysisContext context)
    {
        if (!context.IsAvailable || context.EntryPointNode == null)
            return [];

        var compilation = context.CompilationResult!;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── 1. Inline app.Use(lambda) ─────────────────────────────────────────
        foreach (var useInvocation in InvocationMatcher.FindInvocations(context, "Use"))
        {
            var args = useInvocation.ArgumentList.Arguments;
            if (args.Count == 0)
                continue;

            // Find lambda expressions in the arguments
            foreach (var arg in args)
            {
                var lambdaBody = GetLambdaBody(arg.Expression);
                if (lambdaBody == null)
                    continue;

                CollectHeaderNames(lambdaBody, result, seen, compilation.Compilation);
            }
        }

        // ── 2. app.UseMiddleware<T>() ─────────────────────────────────────────
        foreach (var useMiddlewareInvocation in InvocationMatcher.FindInvocations(context, "UseMiddleware"))
        {
            var typeName = ExtractGenericTypeArgument(useMiddlewareInvocation);
            if (string.IsNullOrWhiteSpace(typeName))
                continue;

            // Search all syntax trees in the compilation for the class
            var classDecl = FindMiddlewareClassBody(typeName!, compilation.SyntaxTrees);
            if (classDecl == null)
                continue;

            // Scan only InvokeAsync/Invoke method bodies to avoid false positives
            // from constructors, property initializers, and helper methods.
            var invokeMethods = classDecl.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text is "InvokeAsync" or "Invoke");

            foreach (var method in invokeMethods)
                CollectHeaderNames(method, result, seen, compilation.Compilation);
        }

        return result;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Header name collection
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="scope"/> for header-mutation statements and collects
    /// the string literal header names into <paramref name="result"/>.
    /// Two patterns are recognised:
    /// <list type="bullet">
    ///   <item><c>*.Response.Headers.Append/Add/TryAdd(name, ...)</c> method calls.</item>
    ///   <item><c>*.Response.Headers["name"] = value</c> indexer assignments.</item>
    /// </list>
    /// Header names that are string literals, literal-only interpolated strings, or
    /// in-project compile-time constants are extracted. Others are skipped with a warning.
    /// </summary>
    /// <param name="compilation">
    /// Optional compilation for resolving in-project compile-time constant header names
    /// (e.g. <c>HeaderNames.RequestId</c>). Pass <see langword="null"/> to skip semantic resolution.
    /// </param>
    private static void CollectHeaderNames(
        SyntaxNode scope,
        List<string> result,
        HashSet<string> seen,
        CSharpCompilation? compilation)
    {
        // ── Pattern A: method calls (.Append/.Add/.TryAdd) ────────────────────
        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax mae)
                continue;

            // Method must be Append, Add, or TryAdd
            var methodName = mae.Name.Identifier.Text;
            if (!HeaderMutationMethods.Contains(methodName))
                continue;

            // The receiver must end with "Response.Headers" — check structurally to
            // avoid ToString() allocation per-invocation in the hot scan loop.
            if (mae.Expression is not MemberAccessExpressionSyntax headersAccess
                || headersAccess.Name.Identifier.Text != "Headers")
                continue;

            if (headersAccess.Expression is not MemberAccessExpressionSyntax responseAccess
                || responseAccess.Name.Identifier.Text != "Response")
                continue;

            // First argument must be a string (the header name)
            var args = invocation.ArgumentList.Arguments;
            if (args.Count == 0)
                continue;

            var firstArg = args[0].Expression;
            var headerName = InvocationMatcher.GetStringValue(firstArg, compilation);

            if (headerName != null)
            {
                if (!seen.Contains(headerName))
                {
                    seen.Add(headerName);
                    result.Add(headerName);
                }
            }
            else
            {
                Console.Error.WriteLine(
                    $"Warning: Response.Headers.{methodName}() call with non-literal header name — skipped.");
            }
        }

        // ── Pattern B: indexer assignments (Response.Headers["Name"] = value) ─
        foreach (var elementAccess in scope.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            // Must be the left side of an assignment expression.
            if (elementAccess.Parent is not AssignmentExpressionSyntax assignment
                || assignment.Left != elementAccess)
                continue;

            // Receiver must end with "Response.Headers".
            if (elementAccess.Expression is not MemberAccessExpressionSyntax headersAccess
                || headersAccess.Name.Identifier.Text != "Headers")
                continue;

            if (headersAccess.Expression is not MemberAccessExpressionSyntax responseAccess
                || responseAccess.Name.Identifier.Text != "Response")
                continue;

            // First argument of the indexer is the header name.
            if (elementAccess.ArgumentList.Arguments.Count < 1)
                continue;

            var indexArg = elementAccess.ArgumentList.Arguments[0].Expression;
            var headerName = InvocationMatcher.GetStringValue(indexArg, compilation);

            if (headerName != null)
            {
                if (!seen.Contains(headerName))
                {
                    seen.Add(headerName);
                    result.Add(headerName);
                }
            }
            else
            {
                Console.Error.WriteLine(
                    $"Warning: Response.Headers[...] assignment with non-literal header name — skipped.");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Generic type argument extraction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the first generic type argument name from a
    /// <c>UseMiddleware&lt;T&gt;()</c> invocation.
    /// Returns <see langword="null"/> if the invocation is not generic.
    /// </summary>
    private static string? ExtractGenericTypeArgument(InvocationExpressionSyntax invocation)
    {
        // Handle: app.UseMiddleware<T>() — MemberAccessExpressionSyntax with GenericNameSyntax
        if (invocation.Expression is MemberAccessExpressionSyntax mae &&
            mae.Name is GenericNameSyntax genMember)
        {
            var typeArgs = genMember.TypeArgumentList.Arguments;
            if (typeArgs.Count > 0)
                return GetUnqualifiedTypeName(typeArgs[0]);
        }

        // Handle: UseMiddleware<T>() — bare GenericNameSyntax (less common)
        if (invocation.Expression is GenericNameSyntax gen)
        {
            var typeArgs = gen.TypeArgumentList.Arguments;
            if (typeArgs.Count > 0)
                return GetUnqualifiedTypeName(typeArgs[0]);
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Middleware class lookup
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches all syntax trees in <paramref name="syntaxTrees"/> for a class
    /// named <paramref name="className"/> and returns its
    /// <see cref="ClassDeclarationSyntax"/>.
    /// Returns <see langword="null"/> if the class cannot be found.
    /// The caller is responsible for filtering to <c>InvokeAsync</c>/<c>Invoke</c>
    /// method bodies before scanning for header mutations.
    /// </summary>
    private static ClassDeclarationSyntax? FindMiddlewareClassBody(
        string className,
        IReadOnlyList<Microsoft.CodeAnalysis.SyntaxTree> syntaxTrees)
    {
        foreach (var tree in syntaxTrees)
        {
            var root = tree.GetRoot();

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!string.Equals(classDecl.Identifier.Text, className, StringComparison.Ordinal))
                    continue;

                return classDecl;
            }
        }

        return null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Lambda body extraction
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the body node of a lambda expression (parenthesized or simplified),
    /// or <see langword="null"/> if <paramref name="expression"/> is not a lambda.
    /// </summary>
    private static SyntaxNode? GetLambdaBody(ExpressionSyntax expression)
    {
        // Strip parentheses
        while (expression is ParenthesizedExpressionSyntax paren)
            expression = paren.Expression;

        return expression switch
        {
            ParenthesizedLambdaExpressionSyntax pl => (SyntaxNode?)pl.Body,
            SimpleLambdaExpressionSyntax sl        => sl.Body,
            AnonymousMethodExpressionSyntax am      => am.Body,
            _                                      => null,
        };
    }
}
