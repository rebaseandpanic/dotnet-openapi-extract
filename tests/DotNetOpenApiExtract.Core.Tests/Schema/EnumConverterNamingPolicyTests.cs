using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Extraction;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.SourceAnalysis;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.OpenApi;
using ModernApi.Models.Keywords;
using Newtonsoft.Json.Serialization;
using Xunit;
using CoreNamingPolicy = DotNetOpenApiExtract.Core.JsonNamingPolicy;
using StjNamingPolicy = System.Text.Json.JsonNamingPolicy;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// A naming policy passed to a global string-enum converter (System.Text.Json's
/// <c>JsonStringEnumConverter(policy)</c>, Newtonsoft's <c>camelCaseText</c> or naming strategy) names
/// the enum members after the member attribute the converter reads; an expression the extractor
/// cannot read leaves the member names and gives one warning.
/// </summary>
public class EnumConverterNamingPolicyTests
{
    private static SourceAnalysisContext Context(string body)
    {
        var tree = CSharpSyntaxTree.ParseText(
            $"builder.Services.AddControllers().AddJsonOptions(o => {{ {body} }});",
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create("TestAssembly", [tree],
            options: new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        return new SourceAnalysisContext(new SourceCompilationResult("/inline", compilation, [tree]),
            ((CSharpSyntaxTree)tree).GetCompilationUnitRoot());
    }

    public static TheoryData<string, CoreNamingPolicy?> Registrations => new()
    {
        { "new JsonStringEnumConverter()", null },
        { "new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)", CoreNamingPolicy.CamelCase },
        { "new JsonStringEnumConverter(namingPolicy: JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)", CoreNamingPolicy.SnakeCaseLower },
        { "new JsonStringEnumConverter<Tint>(JsonNamingPolicy.SnakeCaseUpper)", CoreNamingPolicy.SnakeCaseUpper },
        { "new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower)", CoreNamingPolicy.KebabCaseLower },
        { "new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseUpper)", CoreNamingPolicy.KebabCaseUpper },
        { "new JsonStringEnumConverter(null)", null },
        { "new StringEnumConverter(camelCaseText: true)", CoreNamingPolicy.CamelCase },
        { "new StringEnumConverter(new CamelCaseNamingStrategy())", CoreNamingPolicy.CamelCase },
        { "new StringEnumConverter(typeof(SnakeCaseNamingStrategy))", CoreNamingPolicy.SnakeCaseLower },
        { "new StringEnumConverter { NamingStrategy = new KebabCaseNamingStrategy() }", CoreNamingPolicy.KebabCaseLower },
        { "new StringEnumConverter()", null },
    };

    [Theory]
    [MemberData(nameof(Registrations))]
    public void Extractor_ReadsTheConvertersNamingPolicy(string creation, CoreNamingPolicy? expected)
    {
        var diagnostics = new List<ExtractionDiagnostic>();
        var result = JsonOptionsExtractor.Extract(Context($"o.JsonSerializerOptions.Converters.Add({creation});"), diagnostics.Add);

        result.Mvc.GlobalConverterTypeNames.Should().ContainSingle();
        result.Mvc.GlobalConverterEnumNamingPolicies.Should().Equal(expected);
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("new JsonStringEnumConverter(myPolicy)", "myPolicy")]
    [InlineData("new StringEnumConverter(new MyNamingStrategy())", "new MyNamingStrategy()")]
    public void UnreadablePolicy_LeavesMemberNames_OneWarning(string creation, string expression)
    {
        var diagnostics = new List<ExtractionDiagnostic>();
        var result = JsonOptionsExtractor.Extract(Context($"o.JsonSerializerOptions.Converters.Add({creation});"), diagnostics.Add);

        result.Mvc.GlobalConverterEnumNamingPolicies.Should().Equal([null]);
        var warning = diagnostics.Should().ContainSingle().Which;
        warning.Code.Should().Be(ExtractionDiagnosticCodes.JsonOptionsUnknownConverterNamingPolicy);
        warning.Subjects[1].Should().Be(expression);
    }

    private static string[] EnumValues(string converter, CoreNamingPolicy policy)
    {
        var generator = new SchemaGenerator(new SchemaOptions
        {
            GlobalConverterTypeNames = [converter],
            GlobalConverterEnumNamingPolicies = [policy],
        });
        var json = JsonNode.Parse(generator.GenerateSchema(typeof(Tint))
            .SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!;
        return json["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
    }

    public static TheoryData<CoreNamingPolicy> StjPolicies =>
        [CoreNamingPolicy.CamelCase, CoreNamingPolicy.SnakeCaseLower, CoreNamingPolicy.SnakeCaseUpper, CoreNamingPolicy.KebabCaseLower, CoreNamingPolicy.KebabCaseUpper];

    [Theory]
    [MemberData(nameof(StjPolicies))]
    public void StjPolicy_IsAppliedAfterTheMemberAttribute_AsSystemTextJsonWrites(CoreNamingPolicy policy)
    {
        var stjPolicy = policy switch
        {
            CoreNamingPolicy.CamelCase      => StjNamingPolicy.CamelCase,
            CoreNamingPolicy.SnakeCaseLower => StjNamingPolicy.SnakeCaseLower,
            CoreNamingPolicy.SnakeCaseUpper => StjNamingPolicy.SnakeCaseUpper,
            CoreNamingPolicy.KebabCaseLower => StjNamingPolicy.KebabCaseLower,
            _                               => StjNamingPolicy.KebabCaseUpper,
        };
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(stjPolicy) } };
        var written = Enum.GetValues<Tint>().Select(v => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(v, options))!).ToArray();

        EnumValues(typeof(JsonStringEnumConverter).FullName!, policy).Should().Equal(written);
        written.Should().Contain("ruby", because: "[JsonStringEnumMemberName] is not renamed by the policy");
    }

    [Fact]
    public void NewtonsoftStrategy_IsAppliedAfterEnumMember_AsNewtonsoftWrites()
    {
        var converter = new Newtonsoft.Json.Converters.StringEnumConverter(new CamelCaseNamingStrategy());
        var written = Enum.GetValues<Tint>()
            .Select(v => JsonSerializer.Deserialize<string>(Newtonsoft.Json.JsonConvert.SerializeObject(v, converter))!).ToArray();

        EnumValues(typeof(Newtonsoft.Json.Converters.StringEnumConverter).FullName!, CoreNamingPolicy.CamelCase).Should().Equal(written);
        written.Should().Contain("leaf");
    }

    [Fact]
    public void ProgramCsPolicy_ReachesTheDocument()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);
        var options = VersionedDocumentHarness.ModernApiOptions();
        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = options.AssemblyPath,
            XmlPath      = options.XmlPath,
            SourceRoot   = tempDir.Path,
        });
        var json = JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, CancellationToken.None)
            .GetAwaiter().GetResult())!;

        // A Tint property without a converter of its own follows the global converter.
        var stj = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter(StjNamingPolicy.SnakeCaseUpper) } };
        var written = Enum.GetValues<Tint>().Select(v => JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(v, stj))!);
        json["components"]!["schemas"]!["EnumWireNamesModel"]!["properties"]!["numeric"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>()).Should().Equal(written);
    }

    // ── Newtonsoft: the last assignment wins, with the setters' semantics ───────

    private static string[] NewtonsoftWrites(Newtonsoft.Json.Converters.StringEnumConverter converter) =>
        Enum.GetValues<Tint>().Select(v => JsonSerializer.Deserialize<string>(Newtonsoft.Json.JsonConvert.SerializeObject(v, converter))!).ToArray();

#pragma warning disable CS0618 // CamelCaseText is obsolete in Newtonsoft 13 but still honoured
    public static TheoryData<string, Func<Newtonsoft.Json.Converters.StringEnumConverter>> NewtonsoftCombinations => new()
    {
        { "new StringEnumConverter(new CamelCaseNamingStrategy()) { CamelCaseText = false }",
            () => new(new CamelCaseNamingStrategy()) { CamelCaseText = false } },
        { "new StringEnumConverter(new SnakeCaseNamingStrategy()) { CamelCaseText = false }",
            () => new(new SnakeCaseNamingStrategy()) { CamelCaseText = false } },
        { "new StringEnumConverter(camelCaseText: true) { NamingStrategy = null }",
            () => new(camelCaseText: true) { NamingStrategy = null } },
        { "new StringEnumConverter(new SnakeCaseNamingStrategy()) { CamelCaseText = true }",
            () => new(new SnakeCaseNamingStrategy()) { CamelCaseText = true } },
        { "new StringEnumConverter { NamingStrategy = new KebabCaseNamingStrategy(), CamelCaseText = true }",
            () => new() { NamingStrategy = new KebabCaseNamingStrategy(), CamelCaseText = true } },
        { "new StringEnumConverter(camelCaseText: true) { NamingStrategy = new SnakeCaseNamingStrategy() }",
            () => new(camelCaseText: true) { NamingStrategy = new SnakeCaseNamingStrategy() } },
        { "new StringEnumConverter(typeof(KebabCaseNamingStrategy)) { CamelCaseText = false }",
            () => new(typeof(KebabCaseNamingStrategy)) { CamelCaseText = false } },
    };
#pragma warning restore CS0618

    [Theory]
    [MemberData(nameof(NewtonsoftCombinations))]
    public void NewtonsoftConstructorAndInitializer_GiveTheNamesNewtonsoftWrites(
        string creation, Func<Newtonsoft.Json.Converters.StringEnumConverter> converter)
    {
        var diagnostics = new List<ExtractionDiagnostic>();
        var result = JsonOptionsExtractor.Extract(Context($"o.JsonSerializerOptions.Converters.Add({creation});"), diagnostics.Add);
        var generator = new SchemaGenerator(new SchemaOptions
        {
            GlobalConverterTypeNames = result.Mvc.GlobalConverterTypeNames,
            GlobalConverterEnumNamingPolicies = result.Mvc.GlobalConverterEnumNamingPolicies,
        });
        var json = JsonNode.Parse(generator.GenerateSchema(typeof(Tint))
            .SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!;

        json["enum"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(NewtonsoftWrites(converter()));
        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("new StringEnumConverter(new CamelCaseNamingStrategy()) { NamingStrategy = myStrategy }", true)]
    [InlineData("new StringEnumConverter(myStrategy) { NamingStrategy = new SnakeCaseNamingStrategy() }", false)]
    [InlineData("new StringEnumConverter { CamelCaseText = flag }", true)]
    public void LaterUnreadableStrategy_ClearsTheResult_WithAWarning(string creation, bool unreadable)
    {
        var diagnostics = new List<ExtractionDiagnostic>();
        var result = JsonOptionsExtractor.Extract(Context($"o.JsonSerializerOptions.Converters.Add({creation});"), diagnostics.Add);

        if (unreadable)
        {
            result.Mvc.GlobalConverterEnumNamingPolicies.Should().Equal([null]);
            diagnostics.Should().ContainSingle().Which.Code.Should().Be(ExtractionDiagnosticCodes.JsonOptionsUnknownConverterNamingPolicy);
        }
        else
        {
            result.Mvc.GlobalConverterEnumNamingPolicies.Should().Equal(CoreNamingPolicy.SnakeCaseLower);
            diagnostics.Should().BeEmpty(because: "a later readable strategy replaces the unreadable one");
        }
    }
}
