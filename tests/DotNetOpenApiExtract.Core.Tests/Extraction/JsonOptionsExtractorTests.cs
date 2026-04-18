using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json.Serialization;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Extraction;

/// <summary>
/// Unit tests for <see cref="JsonOptionsExtractor"/>.
/// </summary>
public class JsonOptionsExtractorTests
{
    // ────────────────────────────────────────────────────────────────���─────────
    // 1. Empty context → empty result
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ContextUnavailable_ReturnsEmpty()
    {
        var result = JsonOptionsExtractor.Extract(SourceAnalysisContext.Empty);

        result.PropertyNamingPolicy.Should().BeNull();
        result.DictionaryKeyPolicy.Should().BeNull();
        result.DefaultIgnoreCondition.Should().BeNull();
        result.NumberHandling.Should().BeNull();
        result.GlobalConverterTypeNames.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. ConfigureHttpJsonOptions — PropertyNamingPolicy = CamelCase
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ConfigureHttpJsonOptions_PropertyNamingPolicy_CamelCase()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. ConfigureHttpJsonOptions — PropertyNamingPolicy = SnakeCaseLower
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ConfigureHttpJsonOptions_PropertyNamingPolicy_SnakeCaseLower()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. AddJsonOptions — PropertyNamingPolicy = CamelCase
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_AddJsonOptions_PropertyNamingPolicy_CamelCase()
    {
        var source = """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. PropertyNamingPolicy = null → Preserve
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_PropertyNamingPolicy_Null_ReturnsPreserve()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = null;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.Preserve);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. DictionaryKeyPolicy detected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_DictionaryKeyPolicy_Detected()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.DictionaryKeyPolicy.Should().Be(JsonNamingPolicy.SnakeCaseLower);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. DefaultIgnoreCondition = WhenWritingNull
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_DefaultIgnoreCondition_WhenWritingNull()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.WhenWritingNull);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. NumberHandling — single flag
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NumberHandling_SingleFlag()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.NumberHandling.Should().Be(JsonNumberHandling.AllowReadingFromString);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. NumberHandling — bitwise OR combination
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NumberHandling_BitwiseOr()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.NumberHandling.Should().Be(
            JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9b. NumberHandling — three-flag bitwise OR combination
    //
    // Verifies that the recursive ParseNumberHandling handles
    // (A | B) | C correctly (left-associative binary trees).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NumberHandling_ThreeFlagBitwiseOr()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.NumberHandling =
                    JsonNumberHandling.AllowReadingFromString
                    | JsonNumberHandling.WriteAsString
                    | JsonNumberHandling.AllowNamedFloatingPointLiterals;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        var expected =
            JsonNumberHandling.AllowReadingFromString
            | JsonNumberHandling.WriteAsString
            | JsonNumberHandling.AllowNamedFloatingPointLiterals;

        result.NumberHandling.Should().Be(expected,
            because: "all three flags must be OR-combined when chained with bitwise OR");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7b. DefaultIgnoreCondition = Always — extractor parses it
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_DefaultIgnoreCondition_Always()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Always;
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.Always,
            because: "Always must be detected syntactically — even if SchemaGenerator does not yet act on it");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. Converters.Add — emits type name
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ConvertersAdd_EmitsTypeName()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.GlobalConverterTypeNames.Should().ContainSingle()
            .Which.Should().Contain("JsonStringEnumConverter",
                because: "the syntactic type name should be captured");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. Converters.Add — multiple converters collected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ConvertersAdd_MultipleConverters_AllCollected()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
                o.SerializerOptions.Converters.Add(new DateTimeOffsetConverter());
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.GlobalConverterTypeNames.Should().HaveCount(2);
        result.GlobalConverterTypeNames.Should().Contain(n => n.Contains("JsonStringEnumConverter"));
        result.GlobalConverterTypeNames.Should().Contain(n => n.Contains("DateTimeOffsetConverter"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 12. Non-literal assignment → skipped (no throw)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_NonLiteralAssignment_Skipped()
    {
        var source = """
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = GetPolicy();
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        // Non-literal assignment should be silently skipped — no exception, no value
        result.PropertyNamingPolicy.Should().BeNull(
            because: "a non-literal assignment cannot be resolved statically");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Extra: all naming policy values round-trip
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SnakeCaseLower", JsonNamingPolicy.SnakeCaseLower)]
    [InlineData("SnakeCaseUpper", JsonNamingPolicy.SnakeCaseUpper)]
    [InlineData("KebabCaseLower", JsonNamingPolicy.KebabCaseLower)]
    [InlineData("KebabCaseUpper", JsonNamingPolicy.KebabCaseUpper)]
    public void Extract_AllNamingPolicies_Detected(string memberName, JsonNamingPolicy expected)
    {
        var source = $$"""
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.{{memberName}};
            });
            """;

        var context = BuildContext(source);
        var result = JsonOptionsExtractor.Extract(context);

        result.PropertyNamingPolicy.Should().Be(expected);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // I3. Semantic-model FQN path: resolved name must NOT contain "global::" prefix
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Extract_ConvertersAdd_WithSemanticModel_ReturnsFqnWithoutGlobalPrefix()
    {
        // Arrange: compile WITH real STJ metadata reference so GetSymbolInfo succeeds.
        // This exercises TryGetFqnFromSemanticModel and validates it strips "global::".
        // The source uses ConfigureHttpJsonOptions so the extractor finds the lambda body
        // and processes the Converters.Add call inside it.
        var source = """
            using System.Text.Json.Serialization;

            var builder = WebApplication.CreateBuilder(args);
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            """;

        var ct = Xunit.TestContext.Current.CancellationToken;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: ct);

        // Load STJ assembly so the semantic model can resolve JsonStringEnumConverter.
        var stdlibRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
        var stjRef    = MetadataReference.CreateFromFile(typeof(JsonStringEnumConverter).Assembly.Location);

        var compilation = CSharpCompilation.Create(
            "TestAssemblyWithRefs",
            syntaxTrees: [tree],
            references: [stdlibRef, stjRef],
            options: new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult("/inline", compilation, [tree]);
        var root = ((CSharpSyntaxTree)tree).GetCompilationUnitRoot(ct);
        var context = new SourceAnalysisContext(compilationResult, root);

        // Act
        var result = JsonOptionsExtractor.Extract(context);

        // Assert: at least one converter was captured (syntactic fallback also acceptable)
        result.GlobalConverterTypeNames.Should().NotBeEmpty(
            because: "Converters.Add(new JsonStringEnumConverter()) must be detected");

        // The critical W1 assertion: no name must carry the "global::" pseudo-qualifier.
        foreach (var name in result.GlobalConverterTypeNames)
        {
            name.Should().NotStartWith("global::",
                because: "FQN produced by ToDisplayString must omit the global:: namespace qualifier " +
                         "so it matches plain FQN keys used by JsonConverterRegistry");
        }

        // When semantic resolution succeeds the name should be the full, plain FQN.
        result.GlobalConverterTypeNames.Should().Contain(
            n => n.Contains("JsonStringEnumConverter"),
            because: "the converter type name must be captured");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SourceAnalysisContext BuildContext(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees: [tree],
            options: new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.ConsoleApplication));

        var compilationResult = new SourceCompilationResult("/inline", compilation, [tree]);
        var root = ((CSharpSyntaxTree)tree).GetCompilationUnitRoot();

        return new SourceAnalysisContext(compilationResult, root);
    }
}
