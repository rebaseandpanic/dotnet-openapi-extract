using AwesomeAssertions;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

/// <summary>
/// Unit tests for <see cref="TypeSyntaxHelper"/>.
/// </summary>
public class TypeSyntaxHelperTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // GetUnqualifiedTypeName
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetUnqualifiedTypeName_SimpleIdentifier_ReturnsSelf()
    {
        // Foo
        var typeSyntax = ParseTypeSyntax("Foo");
        TypeSyntaxHelper.GetUnqualifiedTypeName(typeSyntax).Should().Be("Foo");
    }

    [Fact]
    public void GetUnqualifiedTypeName_OneLevel_QualifiedName_ReturnsRight()
    {
        // N.X
        var typeSyntax = ParseTypeSyntax("N.X");
        TypeSyntaxHelper.GetUnqualifiedTypeName(typeSyntax).Should().Be("X");
    }

    [Fact]
    public void GetUnqualifiedTypeName_TwoLevel_QualifiedName_ReturnsRightmost()
    {
        // N.M.X
        var typeSyntax = ParseTypeSyntax("N.M.X");
        TypeSyntaxHelper.GetUnqualifiedTypeName(typeSyntax).Should().Be("X");
    }

    [Fact]
    public void GetUnqualifiedTypeName_FourLevel_QualifiedName_ReturnsRightmost()
    {
        // Microsoft.OpenApi.OpenApiSecurityScheme (3 levels — real production pattern)
        var typeSyntax = ParseTypeSyntax("Microsoft.OpenApi.OpenApiSecurityScheme");
        TypeSyntaxHelper.GetUnqualifiedTypeName(typeSyntax).Should().Be("OpenApiSecurityScheme");
    }

    [Fact]
    public void GetUnqualifiedTypeName_AliasQualified_ReturnsAliasName()
    {
        // global::Foo — AliasQualifiedNameSyntax
        var typeSyntax = ParseTypeSyntax("global::Foo");
        TypeSyntaxHelper.GetUnqualifiedTypeName(typeSyntax).Should().Be("Foo");
    }

    [Fact]
    public void GetUnqualifiedTypeName_GenericName_ReturnsBaseIdentifier()
    {
        // List<T>
        var typeSyntax = ParseTypeSyntax("List<int>");
        TypeSyntaxHelper.GetUnqualifiedTypeName(typeSyntax).Should().Be("List");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetEnumReference
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetEnumReference_SimpleTwoLevel_ReturnsTypAndMember()
    {
        // SecuritySchemeType.ApiKey
        var expr = ParseExpression("SecuritySchemeType.ApiKey");
        var (typeName, memberName) = TypeSyntaxHelper.GetEnumReference(expr);
        typeName.Should().Be("SecuritySchemeType");
        memberName.Should().Be("ApiKey");
    }

    [Fact]
    public void GetEnumReference_ThreeLevel_FQN_ReturnsTypeAndMember()
    {
        // Microsoft.OpenApi.SecuritySchemeType.ApiKey
        var expr = ParseExpression("Microsoft.OpenApi.SecuritySchemeType.ApiKey");
        var (typeName, memberName) = TypeSyntaxHelper.GetEnumReference(expr);
        typeName.Should().Be("SecuritySchemeType");
        memberName.Should().Be("ApiKey");
    }

    [Fact]
    public void GetEnumReference_FourLevel_FQN_ReturnsTypeAndMember()
    {
        // A.B.C.Type.Value — four-level, type = "Type", member = "Value"
        var expr = ParseExpression("A.B.C.Type.Value");
        var (typeName, memberName) = TypeSyntaxHelper.GetEnumReference(expr);
        typeName.Should().Be("Type");
        memberName.Should().Be("Value");
    }

    [Fact]
    public void GetEnumReference_NonMemberAccess_ReturnsBothNull()
    {
        // IdentifierNameSyntax — not a member access
        var expr = ParseExpression("SomeIdentifier");
        var (typeName, memberName) = TypeSyntaxHelper.GetEnumReference(expr);
        typeName.Should().BeNull();
        memberName.Should().BeNull();
    }

    [Fact]
    public void GetEnumReference_LiteralExpression_ReturnsBothNull()
    {
        var expr = ParseExpression("\"hello\"");
        var (typeName, memberName) = TypeSyntaxHelper.GetEnumReference(expr);
        typeName.Should().BeNull();
        memberName.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static TypeSyntax ParseTypeSyntax(string typeText)
    {
        // Parse as a variable declaration to get a TypeSyntax node.
        var source = $"var x = ({typeText})null;";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetCompilationUnitRoot();

        // Find the cast expression's type.
        var castExpr = root.DescendantNodes().OfType<CastExpressionSyntax>().FirstOrDefault();
        if (castExpr != null)
            return castExpr.Type;

        // Fallback: parse as a type argument in a generic invocation.
        var source2 = $"M<{typeText}>();";
        var tree2 = CSharpSyntaxTree.ParseText(source2, new CSharpParseOptions(LanguageVersion.Latest));
        var root2 = tree2.GetCompilationUnitRoot();
        var typeArg = root2.DescendantNodes().OfType<TypeSyntax>().First();
        return typeArg;
    }

    private static ExpressionSyntax ParseExpression(string exprText)
    {
        var source = $"var x = {exprText};";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetCompilationUnitRoot();

        // The variable initializer is our expression.
        var varDecl = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().First();
        return varDecl.Initializer!.Value;
    }
}
