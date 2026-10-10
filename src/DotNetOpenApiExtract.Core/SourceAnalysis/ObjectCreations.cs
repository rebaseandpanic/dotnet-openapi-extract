using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Object creations read from Program.cs in both forms C# allows: <c>new T(…) { … }</c> and the
/// target-typed <c>new(…) { … }</c>, whose type is the type of the place it stands in (the
/// parameter, the property it is assigned to).
/// </summary>
/// <remarks>
/// Program.cs is read syntactically; the compilation has no references to bind
/// <c>OpenApiInfo</c> and the like. A reader therefore recognizes an explicit creation by its type
/// name and a target-typed one by the place it stands in, which the reader knows: the argument of a
/// known call, or the member it is assigned to.
/// </remarks>
internal static class ObjectCreations
{
    /// <summary>The creation behind <paramref name="expression"/> (parentheses stripped), or <see langword="null"/>.</summary>
    public static BaseObjectCreationExpressionSyntax? Of(ExpressionSyntax expression) =>
        Unwrap(expression) as BaseObjectCreationExpressionSyntax;

    /// <summary><paramref name="expression"/> without enclosing parentheses.</summary>
    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax paren)
            expression = paren.Expression;
        return expression;
    }

    /// <summary>
    /// Whether <paramref name="creation"/> creates the type the caller expects: an explicit
    /// <c>new T</c> whose unqualified type name satisfies <paramref name="typeName"/>, or a
    /// target-typed <c>new()</c> — whose type is the expected one, since the caller passes only
    /// creations that stand where that type is required.
    /// </summary>
    public static bool Creates(BaseObjectCreationExpressionSyntax creation, Func<string, bool> typeName) =>
        creation switch
        {
            ObjectCreationExpressionSyntax explicitCreation => typeName(TypeSyntaxHelper.GetUnqualifiedTypeName(explicitCreation.Type)),
            _ => true,
        };

    /// <summary>
    /// The member a creation is assigned to — <c>X</c> in <c>X = new() { … }</c> (an object
    /// initializer) or in <c>a.b.X = new() { … }</c> — or <see langword="null"/>.
    /// </summary>
    public static string? AssignedMember(SyntaxNode creation)
    {
        var node = creation;
        while (node.Parent is ParenthesizedExpressionSyntax paren)
            node = paren;

        return node.Parent is AssignmentExpressionSyntax assignment && assignment.Right == node
            ? assignment.Left switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
                _ => null,
            }
            : null;
    }

    /// <summary>
    /// Whether <paramref name="creation"/> is the argument at <paramref name="index"/> of
    /// <paramref name="invocation"/> (positional; parentheses around it are allowed).
    /// </summary>
    public static bool IsArgument(SyntaxNode creation, InvocationExpressionSyntax invocation, int index)
    {
        var node = creation;
        while (node.Parent is ParenthesizedExpressionSyntax paren)
            node = paren;

        return node.Parent is ArgumentSyntax { NameColon: null } argument
               && argument.Parent == invocation.ArgumentList
               && invocation.ArgumentList.Arguments.IndexOf(argument) == index;
    }
}
