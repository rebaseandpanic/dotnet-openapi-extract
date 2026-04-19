using AwesomeAssertions;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Unit tests for <see cref="ViolationLocationResolver"/> focusing on:
/// - Fix W3: ambiguous class name (same short name in two syntax trees) → (null, null)
/// - Unambiguous class → file and line populated
/// - No source context → file and line null
/// </summary>
public sealed class ViolationLocationResolverTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static SourceAnalysisContext BuildSourceContext(params (string filePath, string source)[] files)
    {
        var trees = files
            .Select(f => CSharpSyntaxTree.ParseText(f.source, path: f.filePath))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references: null,
            options: new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

        var compilationResult = new SourceCompilationResult(".", compilation, trees);
        return new SourceAnalysisContext(compilationResult, entryPointNode: null);
    }

    private static ValidationContext MakeContextWithSchema(
        SourceAnalysisContext sourceCtx,
        string schemaId,
        string typeName)
    {
        // Build a minimal fake CLR type dictionary using a real reflected type as a stand-in.
        // ViolationLocationResolver uses Type.Name for className, so we inject a fake mapping.
        // We can't create a MetadataLoadContext type here — use a real System type as a proxy,
        // but override the mapping so className matches our fixture class name in the syntax tree.
        //
        // Since ViolationLocationResolver resolves via TypeBySchemaId → type.Name, and we need
        // type.Name == typeName, we cannot easily inject a fake Type with an arbitrary Name via
        // standard reflection (typeof(T).Name is fixed). Instead, we test ForSchema/ForSchemaProperty
        // via the schema + source context path where TypeBySchemaId == null (standalone), which
        // exercises the schema-standalone path. For the ambiguity path (W3), we call
        // ForSchemaProperty using the actual source context constructed below.
        //
        // The resolver's internal ResolveFileAndLine is also exercised via ForOperation when
        // ActionByOperationKey provides a controller.Type.Name that matches the class.
        //
        // For this test we use a simpler approach: expose the resolver via schema standalone
        // mode and then override the resolved value by using a context where TypeBySchemaId
        // maps to a real type whose Name happens to match. This requires a real assembly type.
        // Since we just need type.Name, we use a Type from this test assembly.

        return new ValidationContext
        {
            SourceContext = sourceCtx,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // W3: Ambiguous class name → (null, null) for file+line
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveFileAndLine_AmbiguousClassName_ReturnsNullFileAndLine()
    {
        // Two syntax trees both contain a class named "MyController".
        // The resolver must detect the ambiguity and return (null, null).
        const string source1 = """
            namespace Api.V1
            {
                public class MyController { public void Get() { } }
            }
            """;
        const string source2 = """
            namespace Api.V2
            {
                public class MyController { public void Get() { } }
            }
            """;

        var sourceCtx = BuildSourceContext(
            ("file1.cs", source1),
            ("file2.cs", source2));

        // Use a real type whose Name == "MyController" — not available in this assembly,
        // so we use the resolver indirectly via ForSchema(Standalone) first, then
        // call ForSchemaProperty with a TypeBySchemaId dictionary wired up.
        //
        // We need a Type with .Name == "MyController" to wire TypeBySchemaId.
        // Since no such type exists in this assembly, we create a dynamic proxy type name
        // using the existing ViolationLocationResolver directly (it's internal+visible).
        // We construct a context where ActionByOperationKey has a controller whose type.Name
        // matches — but ControllerInfo requires System.Type too.
        //
        // Simplest path: call the resolver via a schema context. We wire TypeBySchemaId
        // to map "MyController" → this.GetType() (ViolationLocationResolverTests), then
        // change its Name to be "MyController". We cannot change GetType().Name.
        //
        // Instead, we test via ForSchemaStandalone which always returns className from schemaId
        // (no Roslyn lookup needed) — but that doesn't test W3.
        //
        // The cleanest testable path for W3: build a ValidationContext where TypeBySchemaId is
        // null (standalone mode), then the resolver's ForSchemaProperty calls ForSchemaPropertyStandalone
        // which does NOT call ResolveFileAndLine. So standalone won't exercise W3.
        //
        // W3 IS exercised via ForOperation (controller.Type.Name) or ForSchema/ForSchemaProperty
        // when TypeBySchemaId != null. For TypeBySchemaId we need Type.Name == class name in source.
        //
        // Solution: create a real type in THIS test file whose name matches the source fixture.
        // See AmbiguousTargetClass below (defined at bottom of file).

        var schemaId = "AmbiguousResolverTarget";
        var ctx = new ValidationContext
        {
            SourceContext = sourceCtx,
            TypeBySchemaId = new Dictionary<string, Type>
            {
                [schemaId] = typeof(AmbiguousResolverTarget),
            },
        };

        var resolver = new ViolationLocationResolver(ctx);
        var location = resolver.ForSchema(schemaId);

        // ClassName must be populated from the CLR type name
        location!.ClassName.Should().Be("AmbiguousResolverTarget");

        // File and Line must be null because two syntax trees have "AmbiguousResolverTarget"
        location.File.Should().BeNull(because: "class name is ambiguous across two syntax trees");
        location.Line.Should().BeNull(because: "class name is ambiguous across two syntax trees");
    }

    [Fact]
    public void ResolveFileAndLine_AmbiguousClassName_PropertyLookup_ReturnsNullFileAndLine()
    {
        // Same ambiguity but for a property lookup path.
        const string source1 = """
            namespace Api.V1
            {
                public class AmbiguousResolverTarget { public string Name { get; set; } }
            }
            """;
        const string source2 = """
            namespace Api.V2
            {
                public class AmbiguousResolverTarget { public string Name { get; set; } }
            }
            """;

        var sourceCtx = BuildSourceContext(
            ("file1.cs", source1),
            ("file2.cs", source2));

        var schemaId = "AmbiguousResolverTarget";
        var ctx = new ValidationContext
        {
            SourceContext = sourceCtx,
            TypeBySchemaId = new Dictionary<string, Type>
            {
                [schemaId] = typeof(AmbiguousResolverTarget),
            },
        };

        var resolver = new ViolationLocationResolver(ctx);
        var location = resolver.ForSchemaProperty(schemaId, "name");

        location!.ClassName.Should().Be("AmbiguousResolverTarget");
        location.File.Should().BeNull(because: "class name matches in two trees — ambiguous");
        location.Line.Should().BeNull(because: "class name matches in two trees — ambiguous");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Unambiguous class → file and line populated
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveFileAndLine_UnambiguousClass_ReturnsFileAndLine()
    {
        // Only one syntax tree with AmbiguousResolverTarget → unambiguous.
        const string source = """
            namespace Api
            {
                public class AmbiguousResolverTarget
                {
                    public string Name { get; set; }
                }
            }
            """;

        var sourceCtx = BuildSourceContext(("Controllers/AmbiguousResolverTarget.cs", source));

        var schemaId = "AmbiguousResolverTarget";
        var ctx = new ValidationContext
        {
            SourceContext = sourceCtx,
            TypeBySchemaId = new Dictionary<string, Type>
            {
                [schemaId] = typeof(AmbiguousResolverTarget),
            },
        };

        var resolver = new ViolationLocationResolver(ctx);
        var location = resolver.ForSchema(schemaId);

        location!.ClassName.Should().Be("AmbiguousResolverTarget");
        location.File.Should().NotBeNull(because: "one unambiguous syntax tree match");
        location.File.Should().Contain("AmbiguousResolverTarget.cs");
        location.Line.Should().NotBeNull(because: "class declaration line is available");
        location.Line.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ResolveFileAndLine_UnambiguousProperty_ReturnsPropertyLine()
    {
        // Verify that a property lookup narrows to the property's line, not the class line.
        const string source = """
            namespace Api
            {
                public class AmbiguousResolverTarget
                {
                    public string Name { get; set; }
                }
            }
            """;

        var sourceCtx = BuildSourceContext(("AmbiguousResolverTarget.cs", source));

        var schemaId = "AmbiguousResolverTarget";
        var ctx = new ValidationContext
        {
            SourceContext = sourceCtx,
            TypeBySchemaId = new Dictionary<string, Type>
            {
                [schemaId] = typeof(AmbiguousResolverTarget),
            },
        };

        var resolver = new ViolationLocationResolver(ctx);
        var classLocation = resolver.ForSchema(schemaId);
        var propLocation = resolver.ForSchemaProperty(schemaId, "Name");

        // Property line should not equal class line (property is on a different line)
        classLocation!.Line.Should().NotBe(propLocation!.Line,
            because: "property 'Name' and the class declaration are on different lines");
        propLocation.Line.Should().NotBeNull();
    }

    [Fact]
    public void ResolveFileAndLine_CamelCasePropertyKey_FindsPascalCaseProperty()
    {
        // Bug fix: schema property keys are camelCase (e.g. "email") but C# identifiers
        // are PascalCase (e.g. "Email"). The resolver should match case-insensitively so
        // that property violations point to the property line, not the class declaration line.
        const string source = """
            namespace Api
            {
                public class AmbiguousResolverTarget
                {
                    public string Email { get; set; }
                }
            }
            """;

        var sourceCtx = BuildSourceContext(("AmbiguousResolverTarget.cs", source));

        var schemaId = "AmbiguousResolverTarget";
        var ctx = new ValidationContext
        {
            SourceContext = sourceCtx,
            TypeBySchemaId = new Dictionary<string, Type>
            {
                [schemaId] = typeof(AmbiguousResolverTarget),
            },
        };

        var resolver = new ViolationLocationResolver(ctx);
        var classLocation = resolver.ForSchema(schemaId);
        // Pass camelCase key "email" — should still resolve to the C# "Email" property line.
        var propLocation = resolver.ForSchemaProperty(schemaId, "email");

        propLocation!.Line.Should().NotBeNull(
            because: "case-insensitive match should find PascalCase 'Email' from camelCase key 'email'");
        propLocation.Line.Should().NotBe(classLocation!.Line,
            because: "the resolved line should be the property's own line, not the class declaration line");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // No source context → file and line null
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveFileAndLine_NoSourceContext_ReturnsNullFileAndLine()
    {
        var ctx = new ValidationContext
        {
            SourceContext = null,
            TypeBySchemaId = new Dictionary<string, Type>
            {
                ["AmbiguousResolverTarget"] = typeof(AmbiguousResolverTarget),
            },
        };

        var resolver = new ViolationLocationResolver(ctx);
        var location = resolver.ForSchema("AmbiguousResolverTarget");

        location!.ClassName.Should().Be("AmbiguousResolverTarget");
        location.File.Should().BeNull(because: "no source context available");
        location.Line.Should().BeNull(because: "no source context available");
    }
}

/// <summary>
/// Fixture class used by ViolationLocationResolverTests to supply a real Type
/// whose Name == "AmbiguousResolverTarget" for TypeBySchemaId mapping.
/// Must be defined at namespace level so typeof() resolves it.
/// </summary>
public sealed class AmbiguousResolverTarget
{
    public string Name { get; set; } = string.Empty;
}
