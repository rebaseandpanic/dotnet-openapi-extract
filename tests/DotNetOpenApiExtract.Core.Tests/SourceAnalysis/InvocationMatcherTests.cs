using AwesomeAssertions;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

public class InvocationMatcherTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 11. FindInvocations — by method name
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_FindInvocations_ByName()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """;

        var root = ParseRoot(source);
        var results = InvocationMatcher.FindInvocations(root, "AddControllers").ToList();

        results.Should().HaveCount(1);
    }

    [Fact]
    public void InvocationMatcher_FindInvocations_MultipleOccurrences()
    {
        var source = """
            app.UseRouting();
            app.UseAuthentication();
            app.UseRouting();
            """;

        var root = ParseRoot(source);
        var results = InvocationMatcher.FindInvocations(root, "UseRouting").ToList();

        results.Should().HaveCount(2);
    }

    [Fact]
    public void InvocationMatcher_FindInvocations_NoMatch_ReturnsEmpty()
    {
        var source = "app.UseRouting();";
        var root = ParseRoot(source);
        var results = InvocationMatcher.FindInvocations(root, "AddControllers").ToList();
        results.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. GetLiteralStringArgument by position
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_ByPosition()
    {
        var source = """app.UsePathBase("/api/v1");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().Be("/api/v1");
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_SecondPosition()
    {
        var source = """options.AddSecurityDefinition("Bearer", null);""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "AddSecurityDefinition").ToList();
        invocations.Should().HaveCount(1);

        var name = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        name.Should().Be("Bearer");
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_OutOfRange_ReturnsNull()
    {
        var source = """app.UsePathBase("/api/v1");""";
        var root = ParseRoot(source);
        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 5);
        value.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 13. GetLiteralStringArgument — non-literal returns null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_NonLiteral_ReturnsNull()
    {
        var source = """app.UsePathBase(config["path"]);""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().BeNull();
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_VariableReference_ReturnsNull()
    {
        var source = """app.UsePathBase(myPath);""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Named argument overload
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_ByName()
    {
        // Method call with a named argument
        var source = """Foo(value: "hello", other: "world");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "Foo").ToList();
        invocations.Should().HaveCount(1);

        var hello = InvocationMatcher.GetLiteralStringArgument(invocations[0], "value");
        hello.Should().Be("hello");

        var world = InvocationMatcher.GetLiteralStringArgument(invocations[0], "other");
        world.Should().Be("world");
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_ByName_NotFound_ReturnsNull()
    {
        var source = """Foo(value: "hello");""";
        var root = ParseRoot(source);
        var invocations = InvocationMatcher.FindInvocations(root, "Foo").ToList();

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], "nonexistent");
        value.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 14. Generic method invocation — matched by base identifier, not full generic name
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_FindInvocations_GenericMethod_MatchedByBaseName()
    {
        // GetSimpleMethodName handles GenericNameSyntax via the gen branch.
        // This branch exists but was untested — a regression here would silently
        // break any caller searching for generic method calls (e.g. AddSingleton<T>).
        var source = """
            services.AddSingleton<IFoo, Foo>();
            services.AddTransient<IBar, Bar>();
            """;

        var root = ParseRoot(source);

        var singleton = InvocationMatcher.FindInvocations(root, "AddSingleton").ToList();
        singleton.Should().HaveCount(1,
            because: "AddSingleton<T,T> has base identifier 'AddSingleton'");

        var transient = InvocationMatcher.FindInvocations(root, "AddTransient").ToList();
        transient.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 15. Verbatim string argument is treated as a string literal
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_VerbatimString_Returned()
    {
        // @"..." is a verbatim string literal. Roslyn represents it as
        // LiteralExpressionSyntax and Token.Value is the unescaped string — same
        // as a regular literal. The existing ExtractStringLiteral helper must handle it.
        var source = """app.UsePathBase(@"/api/v1");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().Be("/api/v1",
            because: "verbatim strings are LiteralExpressionSyntax with the same Token.Value");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 16. nameof() as argument — not a literal, returns null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_Nameof_ReturnsNull()
    {
        // nameof(Foo) is InvocationExpressionSyntax, not LiteralExpressionSyntax.
        // Callers must not assume they get a value for statically-computable expressions.
        var source = """app.UsePathBase(nameof(MyClass));""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().BeNull(
            because: "nameof() is not a string literal — it is an invocation expression");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 17. Interpolated string argument — not a literal, returns null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_InterpolatedString_ReturnsNull()
    {
        // $"..." becomes InterpolatedStringExpressionSyntax, not LiteralExpressionSyntax.
        // This is important: the extractor must not crash on it and must return null.
        var source = """app.UsePathBase($"/api/{version}");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().BeNull(
            because: "interpolated strings with expression holes are not plain string literals");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 18. Interpolated string with only literal content (no holes) — 5.1.4
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_InterpolatedStringLiteralOnly_ReturnsValue()
    {
        // $"/api/v1" — all content is literal text, no {expression} holes.
        var source = """app.UsePathBase($"/api/v1");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().Be("/api/v1",
            because: "an interpolated string with only literal content is equivalent to a plain literal");
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_EmptyInterpolatedString_ReturnsEmptyString()
    {
        // $"" — empty interpolated string with zero content parts.
        var source = """app.UsePathBase($"");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().Be(string.Empty,
            because: "an empty interpolated string has no holes and evaluates to the empty string");
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_VerbatimString_StillWorks()
    {
        // Regression guard: verbatim string must still work after 5.1.4 changes.
        var source = """app.UsePathBase(@"/api/v1");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().Be("/api/v1",
            because: "verbatim string regression — this must not break after 5.1.4 changes");
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_InterpolatedString_WithHole_ReturnsNull()
    {
        // $"/api/{variable}" — has a runtime expression hole, must return null.
        var source = """app.UsePathBase($"/api/{version}");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "UsePathBase").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().BeNull(
            because: "interpolated string with {expression} hole cannot be resolved statically");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 19. Semantic constant resolution via Compilation — 5.1.5
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_ConstantMember_ResolvedViaCompilation()
    {
        // In-project const: HeaderNames.RequestId = "X-Request-Id"
        // The invocation uses the constant member; with a compilation the value should resolve.
        // C# rule: top-level statements must precede type declarations in the same file.
        var source = """
            Headers.Append(HeaderNames.RequestId, "value");

            public static class HeaderNames
            {
                public const string RequestId = "X-Request-Id";
            }
            """;

        var (root, compilation) = ParseWithCompilation(source);

        var invocations = InvocationMatcher.FindInvocations(root, "Append").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0, compilation);
        value.Should().Be("X-Request-Id",
            because: "HeaderNames.RequestId is a compile-time constant resolvable via SemanticModel");
    }

    [Fact]
    public void InvocationMatcher_LiteralStringArgument_ConstantMember_WithoutCompilation_ReturnsNull()
    {
        // Without a compilation, member access to a constant cannot be resolved.
        var source = """Headers.Append(HeaderNames.RequestId, "value");""";
        var root = ParseRoot(source);

        var invocations = InvocationMatcher.FindInvocations(root, "Append").ToList();
        invocations.Should().HaveCount(1);

        var value = InvocationMatcher.GetLiteralStringArgument(invocations[0], 0);
        value.Should().BeNull(
            because: "without a compilation, member-access expressions cannot be resolved statically");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SyntaxNode ParseRoot(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        return tree.GetRoot();
    }

    /// <summary>
    /// Parses <paramref name="source"/> and creates a <see cref="CSharpCompilation"/>
    /// so that semantic constant resolution via <see cref="InvocationMatcher.GetLiteralStringArgument(InvocationExpressionSyntax, int, CSharpCompilation?)"/>
    /// can be exercised in tests.
    /// Uses all runtime DLLs so that primitive types (string, object) are resolvable.
    /// </summary>
    private static (SyntaxNode Root, CSharpCompilation Compilation) ParseWithCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);

        // Include all DLLs from the .NET runtime directory so that string/object
        // type resolution works correctly. The semantic model needs at minimum
        // mscorlib/System.Private.CoreLib to resolve 'string' const values.
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>();
        foreach (var dll in System.IO.Directory.GetFiles(runtimeDir, "*.dll"))
        {
            try { references.Add(MetadataReference.CreateFromFile(dll)); }
            catch { /* skip inaccessible DLLs */ }
        }

        var compilation = CSharpCompilation.Create(
            "TestConsts",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        return (tree.GetRoot(), compilation);
    }
}
