using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Provides syntactic helpers for finding method invocations and extracting
/// literal string arguments from them.
/// </summary>
/// <remarks>
/// All matching is purely syntactic (no semantic resolution). This is fast and
/// sufficient for the common patterns like <c>AddSecurityDefinition("Bearer", ...)</c>
/// or <c>app.UsePathBase("/api/v1")</c> where the method name uniquely identifies
/// the call site.
/// </remarks>
public static class InvocationMatcher
{
    /// <summary>
    /// Finds all invocation expressions within <paramref name="scope"/> whose
    /// called method has the given simple (unqualified) name.
    /// </summary>
    /// <param name="scope">
    /// The root <see cref="SyntaxNode"/> to search (e.g. a <see cref="CompilationUnitSyntax"/>
    /// for a whole file, or a <see cref="MethodDeclarationSyntax"/> for a single method).
    /// </param>
    /// <param name="methodName">
    /// The simple method name to match (e.g. <c>"AddControllers"</c>). The match is
    /// case-sensitive and based on the last identifier in the member-access chain.
    /// </param>
    /// <returns>
    /// All <see cref="InvocationExpressionSyntax"/> nodes whose called method matches
    /// <paramref name="methodName"/>.
    /// </returns>
    public static IEnumerable<InvocationExpressionSyntax> FindInvocations(
        SyntaxNode scope,
        string methodName)
    {
        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = GetSimpleMethodName(invocation.Expression);
            if (string.Equals(name, methodName, StringComparison.Ordinal))
                yield return invocation;
        }
    }

    /// <summary>
    /// Finds all invocation expressions within the entry-point node of
    /// <paramref name="context"/> whose called method has the given simple name.
    /// Uses the pre-built <see cref="SourceAnalysisContext.InvocationsByName"/> index
    /// to avoid repeated tree traversal.
    /// </summary>
    /// <param name="context">The source analysis context whose entry-point to search.</param>
    /// <param name="methodName">
    /// The simple method name to match (case-sensitive).
    /// </param>
    /// <returns>
    /// All <see cref="InvocationExpressionSyntax"/> nodes in the entry-point whose
    /// called method matches <paramref name="methodName"/>. Returns an empty sequence
    /// when <paramref name="context"/> is unavailable.
    /// </returns>
    public static IEnumerable<InvocationExpressionSyntax> FindInvocations(
        SourceAnalysisContext context,
        string methodName)
    {
        return context.InvocationsByName[methodName];
    }

    /// <summary>
    /// Extracts a string literal from the positional argument at <paramref name="position"/>
    /// (zero-based) in <paramref name="invocation"/>.
    /// </summary>
    /// <param name="invocation">The invocation to inspect.</param>
    /// <param name="position">Zero-based index of the positional argument.</param>
    /// <returns>
    /// The literal string value if the argument at <paramref name="position"/> is a
    /// <see cref="LiteralExpressionSyntax"/> string token or an interpolated string
    /// containing only literal text (no <c>{expression}</c> holes);
    /// otherwise <see langword="null"/>.
    /// </returns>
    public static string? GetLiteralStringArgument(
        InvocationExpressionSyntax invocation,
        int position)
    {
        var args = invocation.ArgumentList.Arguments;
        if (position < 0 || position >= args.Count)
            return null;

        return ExtractStringValue(args[position].Expression, compilation: null);
    }

    /// <summary>
    /// Extracts a string literal from the positional argument at <paramref name="position"/>
    /// (zero-based) in <paramref name="invocation"/>, additionally attempting to resolve
    /// compile-time constants via the provided <paramref name="compilation"/> when the
    /// argument is not a direct string literal.
    /// </summary>
    /// <param name="invocation">The invocation to inspect.</param>
    /// <param name="position">Zero-based index of the positional argument.</param>
    /// <param name="compilation">
    /// Optional <see cref="CSharpCompilation"/> used to resolve compile-time constants
    /// (e.g. <c>HeaderNames.RequestId</c>) declared within the same project.
    /// Pass <see langword="null"/> to skip semantic resolution.
    /// </param>
    /// <returns>
    /// The string value if the argument is a string literal, a literal-only interpolated
    /// string, or a compile-time constant resolvable through the semantic model;
    /// otherwise <see langword="null"/>.
    /// </returns>
    public static string? GetLiteralStringArgument(
        InvocationExpressionSyntax invocation,
        int position,
        CSharpCompilation? compilation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (position < 0 || position >= args.Count)
            return null;

        return ExtractStringValue(args[position].Expression, compilation);
    }

    /// <summary>
    /// Extracts a string literal from the named argument with <paramref name="name"/>
    /// in <paramref name="invocation"/>.
    /// </summary>
    /// <param name="invocation">The invocation to inspect.</param>
    /// <param name="name">The parameter name (identifier before the <c>:</c>).</param>
    /// <returns>
    /// The literal string value if the named argument exists and its value is a
    /// <see cref="LiteralExpressionSyntax"/> string token or a literal-only interpolated
    /// string; otherwise <see langword="null"/>.
    /// </returns>
    public static string? GetLiteralStringArgument(
        InvocationExpressionSyntax invocation,
        string name)
    {
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.NameColon == null)
                continue;

            var argName = arg.NameColon.Name.Identifier.Text;
            if (string.Equals(argName, name, StringComparison.Ordinal))
                return ExtractStringValue(arg.Expression, compilation: null);
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a string value from an arbitrary expression, using three
    /// strategies in order:
    /// <list type="number">
    ///   <item>Plain or verbatim string literal (<see cref="LiteralExpressionSyntax"/>).</item>
    ///   <item>Interpolated string whose entire content is literal text (no <c>{…}</c> holes).</item>
    ///   <item>
    ///     Compile-time constant resolution via <see cref="CSharpCompilation.GetSemanticModel"/>
    ///     when <paramref name="compilation"/> is provided (covers in-project
    ///     <c>const string</c> members, e.g. <c>HeaderNames.RequestId</c>).
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="expression">The expression to inspect.</param>
    /// <param name="compilation">
    /// Optional compilation for semantic constant resolution. Pass <see langword="null"/>
    /// to restrict to purely syntactic resolution.
    /// </param>
    /// <returns>The resolved string, or <see langword="null"/> if none of the strategies succeed.</returns>
    public static string? GetStringValue(ExpressionSyntax expression, CSharpCompilation? compilation)
        => ExtractStringValue(expression, compilation);

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the rightmost simple identifier name from a method-call expression.
    /// For <c>builder.Services.AddControllers()</c>, returns <c>"AddControllers"</c>.
    /// </summary>
    public static string? GetSimpleMethodName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.Text,
            IdentifierNameSyntax id          => id.Identifier.Text,
            GenericNameSyntax gen            => gen.Identifier.Text,
            _                                => null,
        };
    }

    /// <summary>
    /// Attempts to extract a string value from an expression: the constant string C# itself would
    /// compute for it. The semantic model evaluates it first (in-project <c>const string</c> members,
    /// <c>nameof</c>, concatenation, constant interpolation); without a compilation, or when the model
    /// cannot bind the expression, literals (plain, verbatim and raw), parentheses, concatenation with
    /// <c>+</c> and interpolation of such strings are folded syntactically.
    /// </summary>
    /// <param name="expression">The expression to resolve.</param>
    /// <param name="compilation">
    /// Optional compilation for semantic constant resolution.
    /// When <see langword="null"/>, only syntactic strategies are attempted.
    /// </param>
    private static string? ExtractStringValue(ExpressionSyntax expression, CSharpCompilation? compilation)
        => SemanticConstant(expression, compilation) ?? FoldString(expression);

    /// <summary>
    /// The string <paramref name="expression"/> stands for when it is built only from string literals:
    /// a literal, a parenthesized one, <c>a + b</c> of two such strings, or an interpolated string whose
    /// holes are such strings without alignment or format. <see langword="null"/> otherwise — a
    /// non-string literal in a hole is not a constant in C# either.
    /// </summary>
    private static string? FoldString(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax lit when lit.Token.Value is string s:
                return s;

            case ParenthesizedExpressionSyntax paren:
                return FoldString(paren.Expression);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                return FoldString(binary.Left) is { } left && FoldString(binary.Right) is { } right ? left + right : null;

            case InterpolatedStringExpressionSyntax interp:
                // In a regular or verbatim interpolation a literal brace is written doubled and the text
                // token keeps it doubled; a raw interpolation ($$"""…""") writes its braces as they are.
                var raw = interp.StringStartToken.IsKind(SyntaxKind.InterpolatedSingleLineRawStringStartToken)
                          || interp.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
                var sb = new StringBuilder();
                foreach (var content in interp.Contents)
                {
                    switch (content)
                    {
                        case InterpolatedStringTextSyntax text:
                            sb.Append(raw ? text.TextToken.ValueText : text.TextToken.ValueText.Replace("{{", "{").Replace("}}", "}"));
                            break;
                        case InterpolationSyntax { AlignmentClause: null, FormatClause: null } hole
                            when FoldString(hole.Expression) is { } part:
                            sb.Append(part);
                            break;
                        default:
                            return null;
                    }
                }

                return sb.ToString();

            default:
                return null;
        }
    }

    /// <summary>The constant string value the semantic model computes for <paramref name="expression"/>, if any.</summary>
    private static string? SemanticConstant(ExpressionSyntax expression, CSharpCompilation? compilation)
    {
        // Compile-time constant via the semantic model: in-project consts, nameof, constant
        // interpolation and concatenation that mix them with literals.
        if (compilation != null)
        {
            try
            {
                var syntaxTree = expression.SyntaxTree;
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var constValue = semanticModel.GetConstantValue(expression);
                if (constValue.HasValue && constValue.Value is string constStr)
                    return constStr;
            }
            catch
            {
                // Semantic resolution is best-effort — fall through to null
            }
        }

        return null;
    }
}
