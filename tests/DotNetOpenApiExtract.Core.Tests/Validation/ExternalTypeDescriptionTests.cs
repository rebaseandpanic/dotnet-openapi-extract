using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// The description rules by the origin of a component's CLR type: a type of the application or of a
/// library it references as a project is the author's to document (<c>schema.description</c>,
/// <c>schema.property-description</c>, errors); a type of a NuGet package or of the framework is not
/// (<c>schema.external-description</c>, <c>schema.external-property-description</c>, warnings that name
/// the assembly and how to supply its XML documentation).
/// </summary>
public sealed class ExternalTypeDescriptionTests
{
    private static ValidationResult Validate()
    {
        OpenApiDocumentBuilder.BuildWithValidation(
            new OpenApiDocumentOptions { AssemblyPath = TestPaths.PackageTypesApiDll },
            new ValidationContext(),
            out var result);
        return result;
    }

    private static string[] Pointers(ValidationResult result, string ruleId) =>
        result.Violations.Where(v => v.RuleId == ruleId).Select(v => v.JsonPointer).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void ProjectAndProjectReferenceTypes_KeepTheErrorRules()
    {
        var result = Validate();

        Pointers(result, "schema.property-description").Should().Equal(
            "#/components/schemas/ContractItem/properties/undocumented",
            "#/components/schemas/Envelope/properties/undocumented",
            "#/components/schemas/LocalMergeSettings/properties/ownUndocumented");
        Pointers(result, "schema.description").Should().BeEmpty();
        result.Violations.Where(v => v.RuleId is "schema.property-description" or "schema.description")
            .Should().OnlyContain(v => v.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void PackageType_IsReportedAsExternal_AsAWarningWithTheAssemblyAndTheRemedy()
    {
        var result = Validate();

        var external = result.Violations
            .Where(v => v.RuleId is "schema.external-description" or "schema.external-property-description")
            .ToList();

        external.Should().NotBeEmpty();
        external.Should().OnlyContain(v => v.JsonPointer.StartsWith("#/components/schemas/JsonMergeSettings", StringComparison.Ordinal)
                                           || v.JsonPointer.StartsWith("#/components/schemas/LocalMergeSettings/properties/", StringComparison.Ordinal));
        external.Should().OnlyContain(v => v.Severity == ValidationSeverity.Warning);
        external.Should().OnlyContain(v => v.Message.Contains("NuGet package assembly 'Newtonsoft.Json'", StringComparison.Ordinal)
                                           && v.Message.Contains("CopyDocumentationFilesFromPackages", StringComparison.Ordinal));
        Pointers(result, "schema.external-description").Should().Equal("#/components/schemas/JsonMergeSettings");

        // A project type inherits the package type's properties: those are external, its own is not.
        // (propertyNameComparison takes the description of System.StringComparison from the framework XML.)
        Pointers(result, "schema.external-property-description")
            .Where(p => p.StartsWith("#/components/schemas/LocalMergeSettings/", StringComparison.Ordinal))
            .Should().Equal(
                "#/components/schemas/LocalMergeSettings/properties/mergeArrayHandling",
                "#/components/schemas/LocalMergeSettings/properties/mergeNullValueHandling");
        result.Violations.Should().NotContain(v => v.JsonPointer.StartsWith("#/components/schemas/JsonMergeSettings", StringComparison.Ordinal)
                                                   && (v.RuleId == "schema.description" || v.RuleId == "schema.property-description"));
    }

    [Fact]
    public void SeverityOverride_MakesExternalDescriptionsErrors()
    {
        OpenApiDocumentBuilder.BuildWithValidation(
            new OpenApiDocumentOptions { AssemblyPath = TestPaths.PackageTypesApiDll },
            new ValidationContext
            {
                SeverityOverrides = new Dictionary<string, ValidationSeverity> { ["schema.external-property-description"] = ValidationSeverity.Error },
            },
            out var result);

        result.Violations.Where(v => v.RuleId == "schema.external-property-description")
            .Should().NotBeEmpty().And.OnlyContain(v => v.Severity == ValidationSeverity.Error);
    }

    // ── The origin by ProjectAssemblyNames ─────────────────────────────────────────────

    private static OpenApiDocument UndescribedComponent() => new()
    {
        Info = new OpenApiInfo { Title = "T", Version = "v1" },
        Components = new OpenApiComponents
        {
            Schemas = new Dictionary<string, IOpenApiSchema>
            {
                ["Version"] = new OpenApiSchema { Type = JsonSchemaType.Object },
            },
        },
    };

    /// <summary>
    /// <c>System.Version</c> is declared in the framework: outside a project of the named assemblies it is
    /// external; without project assemblies (standalone, no <c>*.deps.json</c>) every type is the project's.
    /// </summary>
    [Theory]
    [InlineData(true, "schema.external-description")]
    [InlineData(false, "schema.description")]
    public void TheOrigin_FollowsTheProjectAssemblyNames(bool projectKnown, string reportedBy)
    {
        var context = new ValidationContext
        {
            TypeBySchemaId = new Dictionary<string, Type> { ["Version"] = typeof(Version) },
            ProjectAssemblyNames = projectKnown ? new HashSet<string> { "MyApi" } : null,
        };

        var rules = new IValidationRule[] { new SchemaDescriptionRule(), new SchemaExternalDescriptionRule() };
        rules.SelectMany(r => r.Validate(UndescribedComponent(), context)).Select(v => v.RuleId)
            .Should().Equal([reportedBy]);
    }

    /// <summary>
    /// The advice follows the origin: for a package assembly, copying the package's XML documentation;
    /// for any other assembly outside the project (the framework), passing its XML with <c>--xml</c>.
    /// </summary>
    [Theory]
    [InlineData(true, "CopyDocumentationFilesFromPackages")]
    [InlineData(false, "pass it with --xml")]
    public void TheAdvice_FollowsThePackageAssemblyNames(bool fromPackage, string advice)
    {
        var context = new ValidationContext
        {
            TypeBySchemaId = new Dictionary<string, Type> { ["Version"] = typeof(Version) },
            ProjectAssemblyNames = new HashSet<string> { "MyApi" },
            PackageAssemblyNames = fromPackage ? new HashSet<string> { typeof(Version).Assembly.GetName().Name! } : new HashSet<string>(),
        };

        var message = new SchemaExternalDescriptionRule().Validate(UndescribedComponent(), context).Single().Message;

        message.Should().Contain(advice);
        if (!fromPackage)
            message.Should().NotContain("CopyDocumentationFilesFromPackages");
    }
}
