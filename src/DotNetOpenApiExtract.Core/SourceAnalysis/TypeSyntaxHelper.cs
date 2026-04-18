using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Provides helpers for working with <see cref="TypeSyntax"/> and
/// <see cref="Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax"/> nodes
/// that represent type names and enum member accesses in source code.
/// </summary>
/// <remarks>
/// These helpers are purely syntactic — no semantic resolution is performed.
/// They handle the common production patterns where types and enum values are
/// written with fully-qualified namespace prefixes, e.g.
/// <c>new Microsoft.OpenApi.OpenApiSecurityScheme { ... }</c> or
/// <c>Type = Microsoft.OpenApi.SecuritySchemeType.ApiKey</c>.
/// </remarks>
public static class TypeSyntaxHelper
{
    /// <summary>
    /// Returns the rightmost (unqualified) identifier text from any
    /// <see cref="TypeSyntax"/> variant, unwrapping namespace qualifications
    /// and alias qualifications recursively.
    /// </summary>
    /// <param name="type">The type syntax to inspect.</param>
    /// <returns>
    /// <para>
    /// For <c>IdentifierNameSyntax</c> → the identifier text (e.g. <c>"Foo"</c>).
    /// </para>
    /// <para>
    /// For <c>QualifiedNameSyntax</c> (<c>N.M.X</c>) → recursively the rightmost
    /// identifier, e.g. <c>"X"</c>.
    /// </para>
    /// <para>
    /// For <c>AliasQualifiedNameSyntax</c> (<c>global::X</c>) → the alias name
    /// identifier, e.g. <c>"X"</c>.
    /// </para>
    /// <para>
    /// For <c>GenericNameSyntax</c> (<c>List&lt;T&gt;</c>) → the base identifier
    /// text without type arguments, e.g. <c>"List"</c>.
    /// </para>
    /// <para>
    /// For any other syntax form → falls back to <c>type.ToString()</c>.
    /// </para>
    /// </returns>
    public static string GetUnqualifiedTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id         => id.Identifier.Text,
        QualifiedNameSyntax qual        => GetUnqualifiedTypeName(qual.Right),
        AliasQualifiedNameSyntax alias  => alias.Name.Identifier.Text,
        GenericNameSyntax gen           => gen.Identifier.Text,
        _                               => type.ToString(),
    };

    /// <summary>
    /// Extracts the enum type name and member name from a <see cref="ExpressionSyntax"/>
    /// that represents a member-access enum value, e.g.
    /// <c>SecuritySchemeType.ApiKey</c> or <c>Microsoft.OpenApi.SecuritySchemeType.ApiKey</c>.
    /// </summary>
    /// <param name="expr">The expression to inspect.</param>
    /// <returns>
    /// A tuple of <c>(TypeName, MemberName)</c> where <c>TypeName</c> is the last
    /// identifier before the final <c>.</c> (the enum type short name), and
    /// <c>MemberName</c> is the rightmost identifier (the enum member). Both are
    /// <see langword="null"/> if <paramref name="expr"/> is not a
    /// <see cref="MemberAccessExpressionSyntax"/>.
    /// </returns>
    /// <example>
    /// <para><c>SecuritySchemeType.ApiKey</c> → <c>("SecuritySchemeType", "ApiKey")</c></para>
    /// <para><c>Microsoft.OpenApi.SecuritySchemeType.ApiKey</c> → <c>("SecuritySchemeType", "ApiKey")</c></para>
    /// <para><c>N.M.X.Y</c> → <c>("X", "Y")</c></para>
    /// </example>
    public static (string? TypeName, string? MemberName) GetEnumReference(ExpressionSyntax expr)
    {
        if (expr is not MemberAccessExpressionSyntax mae)
            return (null, null);

        var memberName = mae.Name.Identifier.Text;

        // The expression to the left of the final dot gives the type name.
        // It could be a simple identifier (e.g. SecuritySchemeType) or another
        // MemberAccessExpressionSyntax (e.g. Microsoft.OpenApi.SecuritySchemeType —
        // in which case its .Name is the type short name).
        var typeName = mae.Expression switch
        {
            IdentifierNameSyntax id             => id.Identifier.Text,
            MemberAccessExpressionSyntax inner  => inner.Name.Identifier.Text,
            _                                   => null,
        };

        return (typeName, memberName);
    }
}
