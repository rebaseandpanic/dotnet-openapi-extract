using System.Reflection;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

public class EntryPointFinderTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 8. Top-level statements (SampleApi uses them)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EntryPointFinder_TopLevelStatements_Found()
    {
        var dllPath = TestPaths.SampleApiDll;
        var resolved = SourceRootResolver.TryResolve(dllPath, out var sourceRoot, out var reason);
        resolved.Should().BeTrue(because: reason ?? "no reason");

        var compilationResult = SourceCompiler.Compile(sourceRoot!);

        // SampleApi uses top-level statements → EntryPoint.Name == "<Main>$"
        using var loader = new DotNetOpenApiExtract.Core.Loading.AssemblyLoader(dllPath);
        var entryPoint = loader.Assembly.EntryPoint;

        entryPoint.Should().NotBeNull(
            because: "SampleApi is an executable with an entry point");
        entryPoint!.Name.Should().Be("<Main>$",
            because: "top-level statements produce a synthetic <Main>$ entry point");

        var node = EntryPointFinder.Find(entryPoint, compilationResult.Compilation);

        node.Should().NotBeNull(
            because: "SampleApi/Program.cs uses top-level statements which should be findable");
        node.Should().BeOfType<CompilationUnitSyntax>(
            because: "top-level statements map to a CompilationUnitSyntax");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8b. Top-level statements with legacy "<Main>" entry point name (no "$" suffix)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EntryPointFinder_TopLevelStatements_LegacyMainName_Found()
    {
        // Some compilers (or build configurations) emit "<Main>" without the "$" suffix
        // for top-level statements. The finder must handle both forms.
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.Run();
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var ct = TestContext.Current.CancellationToken;
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: ct);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        // Simulate a "<Main>" (legacy, no $) entry-point stub.
        var entryPoint = new NamedMethodBaseStub("<Main>", "Program");
        var node = EntryPointFinder.Find(entryPoint, compilation);

        node.Should().NotBeNull(
            because: "'<Main>' (without '$') is also a synthetic entry point for top-level statements");
        node.Should().BeOfType<CompilationUnitSyntax>(
            because: "top-level statements map to CompilationUnitSyntax regardless of the '$' suffix");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Conventional Main method
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EntryPointFinder_ConventionalMain_Found()
    {
        var source = """
            class Program
            {
                static void Main(string[] args) { }
            }
            """;

        var compilation = CreateSingleTreeCompilation(source);

        // Use a real MethodInfo: FakeConventionalProgram.Main has Name="Main"
        // and DeclaringType.Name="FakeConventionalProgram" — we need "Program".
        // Build a MethodBase wrapper that provides Name="Main" and DeclaringType.Name="Program".
        var entryPoint = new NamedMethodBaseStub("Main", "Program");

        var node = EntryPointFinder.Find(entryPoint, compilation);

        node.Should().NotBeNull();
        node.Should().BeOfType<MethodDeclarationSyntax>();
        var method = (MethodDeclarationSyntax)node!;
        method.Identifier.Text.Should().Be("Main");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. Entry point not found — returns null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EntryPointFinder_NotFound_ReturnsNull()
    {
        // Compilation with no matching method
        var source = "class Foo { void Bar() {} }";
        var compilation = CreateSingleTreeCompilation(source);

        var entryPoint = new NamedMethodBaseStub("Main", "Program");
        var node = EntryPointFinder.Find(entryPoint, compilation);

        node.Should().BeNull();
    }

    [Fact]
    public void EntryPointFinder_NullEntryPoint_ReturnsNull()
    {
        var source = "class Foo {}";
        var compilation = CreateSingleTreeCompilation(source);

        var node = EntryPointFinder.Find(null, compilation);

        node.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. Nested class containing Main — found by simple type name, not full name
    // ──────────────────────────────────────────────────────────────────────────
    // FindMethodInUnit searches via DescendantNodes().OfType<TypeDeclarationSyntax>()
    // which finds all types regardless of nesting depth. The match is by simple
    // type Name (not full qualified name). Verify the nested Main is reachable.

    [Fact]
    public void EntryPointFinder_NestedClassMain_FoundBySimpleTypeName()
    {
        var source = """
            class Outer
            {
                class Program
                {
                    static void Main(string[] args) { }
                }
            }
            """;

        var compilation = CreateSingleTreeCompilation(source);
        var entryPoint = new NamedMethodBaseStub("Main", "Program");

        var node = EntryPointFinder.Find(entryPoint, compilation);

        // The finder uses simple type name matching so "Program" inside "Outer" is found
        node.Should().NotBeNull(
            because: "FindMethodInUnit descends into nested types");
        node.Should().BeOfType<MethodDeclarationSyntax>();
        var method = (MethodDeclarationSyntax)node!;
        method.Identifier.Text.Should().Be("Main");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. Multiple trees in compilation — Main found in the correct tree
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EntryPointFinder_MultipleFiles_FindsMainInCorrectTree()
    {
        var programSource = """
            class Program
            {
                static void Main(string[] args) { }
            }
            """;
        var otherSource = """
            class Other
            {
                void NotMain() { }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var ct = TestContext.Current.CancellationToken;
        var tree1 = CSharpSyntaxTree.ParseText(otherSource, parseOptions, cancellationToken: ct);
        var tree2 = CSharpSyntaxTree.ParseText(programSource, parseOptions, cancellationToken: ct);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree1, tree2],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));

        var entryPoint = new NamedMethodBaseStub("Main", "Program");
        var node = EntryPointFinder.Find(entryPoint, compilation);

        node.Should().NotBeNull();
        node.Should().BeOfType<MethodDeclarationSyntax>();
        ((MethodDeclarationSyntax)node!).Identifier.Text.Should().Be("Main");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static CSharpCompilation CreateSingleTreeCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        return CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));
    }

    /// <summary>
    /// Minimal <see cref="MethodBase"/> stub that reports a fixed name and a fixed
    /// declaring-type name. Used to avoid implementing the full abstract surface of
    /// <see cref="System.Type"/> without external mocking libraries.
    /// </summary>
    private sealed class NamedMethodBaseStub : MethodBase
    {
        private readonly string _name;
        private readonly string _declaringTypeName;

        public NamedMethodBaseStub(string name, string declaringTypeName)
        {
            _name = name;
            _declaringTypeName = declaringTypeName;
        }

        public override string Name => _name;

        // DeclaringType is only used for its .Name property in EntryPointFinder.
        // We use a real System.Type from the BCL as a carrier and wrap it.
        // The simplest carrier is an anonymous object's type — we override Name via the subclass.
        public override Type? DeclaringType => new DeclaringTypeStub(_declaringTypeName);

        // ── Abstract member stubs (not accessed by the code under test) ──────
        public override MemberTypes MemberType => MemberTypes.Method;
        public override MethodAttributes Attributes => MethodAttributes.Static;
        public override RuntimeMethodHandle MethodHandle => throw new NotSupportedException();
        public override Type ReflectedType => typeof(object);
        public override object[] GetCustomAttributes(bool inherit) => [];
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];
        public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.IL;
        public override ParameterInfo[] GetParameters() => [];
        public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder,
            object?[]? parameters, System.Globalization.CultureInfo? culture)
            => throw new NotSupportedException();
        public override bool IsDefined(Type attributeType, bool inherit) => false;
    }

    /// <summary>
    /// A <see cref="Type"/> whose <see cref="Type.Name"/> returns a custom value.
    /// Inherits from a concrete BCL type to avoid implementing the full abstract surface.
    /// All other members delegate to <see cref="object"/>'s type.
    /// </summary>
    private sealed class DeclaringTypeStub : TypeDelegator
    {
        private readonly string _name;

        public DeclaringTypeStub(string name) : base(typeof(object))
        {
            _name = name;
        }

        public override string Name => _name;
    }
}
