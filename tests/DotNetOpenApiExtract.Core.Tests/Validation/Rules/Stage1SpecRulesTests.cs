using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// The document structure rules of OpenAPI 3.1/3.2: each one reports its violation, accepts a valid
/// document and does not apply to versions without the checked field.
/// </summary>
public sealed class Stage1SpecRulesTests
{
    private const string LicenseRule = "spec.license-identifier-or-url";
    private const string StructureRule = "spec.paths-or-webhooks-or-components";
    private const string ParentDefinedRule = "tag.parent-defined";
    private const string ParentCycleRule = "tag.no-parent-cycle";
    private const string ServerNamesRule = "spec.server-names-unique";

    /// <summary>Runs every rule for <paramref name="version"/>; the rule under test must not have crashed.</summary>
    private static IReadOnlyList<ValidationViolation> Violations(OpenApiDocument document, OpenApiSpecVersion? version, string ruleId)
    {
        var result = CoreValidator.Validate(document, new ValidationContext { OpenApiSpecVersion = version });
        result.SkippedRules.Should().NotContain(ruleId);
        return result.Violations.Where(v => v.RuleId == ruleId).ToList();
    }

    private static OpenApiDocument Document(Action<OpenApiDocument>? configure = null)
    {
        var document = new OpenApiDocument
        {
            Info  = new OpenApiInfo { Title = "T", Version = "1" },
            Paths = new OpenApiPaths(),
        };
        configure?.Invoke(document);
        return document;
    }

    public static TheoryData<OpenApiSpecVersion> AllVersions => [.. VersionedDocumentHarness.Versions];

    // ─────────────────────────────────────────────────────────────────────────
    // spec.license-identifier-or-url
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0, 0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, 1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, 1)]
    public void License_IdentifierAndUrl_ViolatesFrom31(OpenApiSpecVersion version, int expected)
    {
        var document = Document(d => d.Info.License = new OpenApiLicense
        {
            Name = "MIT", Identifier = "MIT", Url = new Uri("https://opensource.org/licenses/MIT"),
        });

        var violations = Violations(document, version, LicenseRule);

        violations.Should().HaveCount(expected);
        violations.Should().AllSatisfy(v =>
        {
            v.JsonPointer.Should().Be("#/info/license");
            v.Severity.Should().Be(ValidationSeverity.Error);
        });
    }

    [Theory]
    [MemberData(nameof(AllVersions))]
    public void License_OnlyOneOfThem_NoViolation(OpenApiSpecVersion version)
    {
        var identifierOnly = Document(d => d.Info.License = new OpenApiLicense { Name = "MIT", Identifier = "MIT" });
        var urlOnly = Document(d => d.Info.License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://opensource.org/licenses/MIT") });

        Violations(identifierOnly, version, LicenseRule).Should().BeEmpty();
        Violations(urlOnly, version, LicenseRule).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // spec.paths-or-webhooks-or-components (a missing object is null, an empty one is present)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Structure_V30_RequiresPaths_EmptyPathsIsPresent()
    {
        var missing = Document(d =>
        {
            d.Paths = null!;
            d.Components = new OpenApiComponents();
        });
        var empty = Document();

        Violations(missing, OpenApiSpecVersion.OpenApi3_0, StructureRule)
            .Should().ContainSingle().Which.JsonPointer.Should().Be("#");
        Violations(empty, OpenApiSpecVersion.OpenApi3_0, StructureRule).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2)]
    public void Structure_V31V32_RequiresOneOfThree_EmptyObjectsArePresent(OpenApiSpecVersion version)
    {
        var none = Document(d => d.Paths = null!);
        var componentsOnly = Document(d =>
        {
            d.Paths = null!;
            d.Components = new OpenApiComponents();
        });
        var webhooksOnly = Document(d =>
        {
            d.Paths = null!;
            d.Webhooks = new Dictionary<string, IOpenApiPathItem>();
        });
        var emptyPaths = Document();

        Violations(none, version, StructureRule).Should().ContainSingle().Which.JsonPointer.Should().Be("#");
        Violations(componentsOnly, version, StructureRule).Should().BeEmpty();
        Violations(webhooksOnly, version, StructureRule).Should().BeEmpty();
        Violations(emptyPaths, version, StructureRule).Should().BeEmpty();
    }

    /// <summary>
    /// Standalone <c>validate</c>: the reader turns a missing <c>paths</c> into an empty one, so the CLI
    /// checks the file itself — a missing object is still reported and an empty one is not, in JSON and YAML.
    /// </summary>
    [Theory]
    [InlineData("openapi.json", """{ "openapi": "3.0.4", "info": { "title": "T", "version": "1" }, "components": { } }""", true)]
    [InlineData("openapi.json", """{ "openapi": "3.0.4", "info": { "title": "T", "version": "1" }, "paths": { } }""", false)]
    [InlineData("openapi.json", """{ "openapi": "3.1.2", "info": { "title": "T", "version": "1" } }""", true)]
    [InlineData("openapi.json", """{ "openapi": "3.1.2", "info": { "title": "T", "version": "1" }, "components": { } }""", false)]
    [InlineData("openapi.json", """{ "openapi": "3.2.0", "info": { "title": "T", "version": "1" }, "webhooks": { } }""", false)]
    [InlineData("openapi.yaml", "openapi: 3.0.4\ninfo:\n  title: T\n  version: '1'\ncomponents: {}\n", true)]
    [InlineData("openapi.yaml", "openapi: 3.1.2\ninfo:\n  title: T\n  version: '1'\npaths: {}\n", false)]
    [InlineData("openapi.yaml", "openapi: 3.2.0\ninfo:\n  title: T\n  version: '1'\n", true)]
    // Multi-document YAML: the loader reads the first document only, and so does the paths check —
    // a malformed second document changes nothing.
    [InlineData("openapi.yaml", "openapi: 3.0.4\ninfo: {title: T, version: '1'}\npaths: {}\n---\n[unterminated\n", false)]
    [InlineData("openapi.yaml", "openapi: 3.0.4\ninfo: {title: T, version: '1'}\ncomponents: {}\n---\n[unterminated\n", true)]
    [InlineData("openapi.yaml", "openapi: 3.1.2\ninfo: {title: T, version: '1'}\n---\npaths: {}\n", true)]
    // Byte order mark and line breaks.
    [InlineData("openapi.yaml", "\uFEFFopenapi: 3.1.2\r\ninfo:\r\n  title: T\r\n  version: '1'\r\npaths: {}\r\n", false)]
    [InlineData("openapi.yaml", "\uFEFFopenapi: 3.1.2\r\ninfo:\r\n  title: T\r\n  version: '1'\r\n", true)]
    [InlineData("openapi.json", "\uFEFF{\r\n  \"openapi\": \"3.0.4\",\r\n  \"info\": { \"title\": \"T\", \"version\": \"1\" },\r\n  \"paths\": { }\r\n}\r\n", false)]
    [InlineData("openapi.json", "\uFEFF{ \"openapi\": \"3.0.4\", \"info\": { \"title\": \"T\", \"version\": \"1\" }, \"components\": { } }", true)]
    // JSON in a .yaml file is read as YAML by the loader, and checked the same way.
    [InlineData("openapi.yaml", """{ "openapi": "3.1.2", "info": { "title": "T", "version": "1" } }""", true)]
    public async Task Structure_Standalone_TellsMissingPathsFromEmpty(string fileName, string content, bool violates)
    {
        using var directory = new TempDirectory();
        var spec = Path.Combine(directory.Path, fileName);
        await File.WriteAllTextAsync(spec, content, TestContext.Current.CancellationToken);

        var result = await CliRunner.RunAsync(["validate", "--spec", spec], directory.Path, TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(violates ? 1 : 0, result.StdErr);
        var report = JsonNode.Parse(result.StdOut)!;
        report["summary"]!["skippedRules"]!.AsArray().Select(r => r!.GetValue<string>()).Should().NotContain(StructureRule);
        report["violations"]!.AsArray().Count(v => (string?)v!["rule"] == StructureRule).Should().Be(violates ? 1 : 0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // tag.parent-defined, tag.no-parent-cycle
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A document with tags declared in order, each with its parent name (or none).</summary>
    private static OpenApiDocument Tags(params (string Name, string? Parent)[] tags) => Document(d =>
    {
        d.Tags = new HashSet<OpenApiTag>();
        foreach (var (name, parent) in tags)
            d.Tags.Add(new OpenApiTag { Name = name, Parent = parent == null ? null : new OpenApiTagReference(parent, d) });
    });

    [Fact]
    public void TagParent_Undeclared_V32_Violates()
    {
        var document = Tags(("root", null), ("child", "missing"));

        Violations(document, OpenApiSpecVersion.OpenApi3_2, ParentDefinedRule)
            .Should().ContainSingle().Which.JsonPointer.Should().Be("#/tags/1/parent");
        Violations(document, OpenApiSpecVersion.OpenApi3_2, ParentCycleRule).Should().BeEmpty();
    }

    public static TheoryData<(string, string?)[], string> Cycles => new()
    {
        { [("a", "b"), ("b", "a")], "#/tags/0/parent" },
        { [("self", "self")], "#/tags/0/parent" },
        // A tag leading into a cycle is not part of it: one violation, at the cycle's first tag.
        { [("lead", "x"), ("x", "y"), ("y", "z"), ("z", "x")], "#/tags/1/parent" },
    };

    [Theory]
    [MemberData(nameof(Cycles))]
    public void TagParent_Cycle_V32_OneViolationPerCycle((string, string?)[] tags, string pointer)
    {
        var document = Tags(tags);

        Violations(document, OpenApiSpecVersion.OpenApi3_2, ParentCycleRule)
            .Should().ContainSingle().Which.JsonPointer.Should().Be(pointer);
        Violations(document, OpenApiSpecVersion.OpenApi3_2, ParentDefinedRule).Should().BeEmpty();
    }

    [Fact]
    public void TagParent_TwoCycles_TwoViolations()
    {
        var document = Tags(("a", "b"), ("b", "a"), ("c", "c"));

        Violations(document, OpenApiSpecVersion.OpenApi3_2, ParentCycleRule)
            .Select(v => v.JsonPointer).Should().Equal("#/tags/0/parent", "#/tags/2/parent");
    }

    [Fact]
    public void TagParent_Hierarchy_V32_NoViolation()
    {
        var document = Tags(("root", null), ("child", "root"), ("grandchild", "child"), ("sibling", "root"));

        Violations(document, OpenApiSpecVersion.OpenApi3_2, ParentDefinedRule).Should().BeEmpty();
        Violations(document, OpenApiSpecVersion.OpenApi3_2, ParentCycleRule).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    public void TagParent_BeforeV32_NotApplied(OpenApiSpecVersion version)
    {
        var document = Tags(("a", "b"), ("b", "a"), ("c", "missing"));

        Violations(document, version, ParentDefinedRule).Should().BeEmpty();
        Violations(document, version, ParentCycleRule).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // spec.server-names-unique
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument Servers(params string?[] names) => Document(d =>
        d.Servers = names.Select((name, i) => new OpenApiServer { Url = $"https://s{i}.example.com", Name = name }).ToList());

    [Fact]
    public void ServerNames_Duplicate_V32_Violates()
    {
        Violations(Servers("prod", "staging", "prod"), OpenApiSpecVersion.OpenApi3_2, ServerNamesRule)
            .Should().ContainSingle().Which.JsonPointer.Should().Be("#/servers/2/name");
    }

    [Fact]
    public void ServerNames_DistinctOrUnnamed_V32_NoViolation()
    {
        Violations(Servers("prod", "staging", null, null), OpenApiSpecVersion.OpenApi3_2, ServerNamesRule).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_0)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1)]
    public void ServerNames_BeforeV32_NotApplied(OpenApiSpecVersion version)
    {
        Violations(Servers("prod", "prod"), version, ServerNamesRule).Should().BeEmpty();
    }
}

/// <summary>
/// The validation version left unset in the context comes from the build options: the tag rules,
/// which apply to 3.2 only, see a Program.cs tag whose parent is not declared.
/// </summary>
public sealed class Stage1SpecRulesBuildTests
{
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, 1)]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, 0)]
    public void BuildWithValidation_VersionFromOptions_DrivesTagParentRule(OpenApiSpecVersion version, int expected)
    {
        using var directory = new TempDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Program.cs"), """
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddTag(new OpenApiTag { Name = "SchemaKeywords", Parent = new OpenApiTagReference("NoSuchTag") });
            });
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath   = TestPaths.ModernApiDll,
            XmlPath        = TestPaths.ModernApiXml,
            SourceRoot     = directory.Path,
            OpenApiVersion = version,
        };

        OpenApiDocumentBuilder.BuildWithValidation(options, new ValidationContext { OpenApiSpecVersion = null }, out var result);

        result.SkippedRules.Should().NotContain("tag.parent-defined");
        result.Violations.Count(v => v.RuleId == "tag.parent-defined").Should().Be(expected);
    }
}
