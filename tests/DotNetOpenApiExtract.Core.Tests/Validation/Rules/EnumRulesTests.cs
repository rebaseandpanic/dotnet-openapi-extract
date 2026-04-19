using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for enum-level validation rules.
/// </summary>
public sealed class EnumRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    private static IList<JsonNode> MakeEnumValues(params string[] values)
        => values
            .Select(v => (JsonNode)JsonValue.Create(v)!)
            .ToList();

    // ─────────────────────────────────────────────────────────────────────────
    // enum.type-description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnumTypeDescription_WhenDescriptionMentionsAllValues_NoViolation()
    {
        var doc = BuildEnumDoc("Active", "Inactive", description: "Status can be Active or Inactive.");
        var rule = new EnumTypeDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void EnumTypeDescription_WhenDescriptionMissing_OneViolation()
    {
        var doc = BuildEnumDoc("Active", "Inactive", description: null);
        var rule = new EnumTypeDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("enum.type-description");
    }

    [Fact]
    public void EnumTypeDescription_WhenDescriptionMissingValue_OneViolation()
    {
        var doc = BuildEnumDoc("Active", "Inactive", description: "Only mentions Active.");
        var rule = new EnumTypeDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("enum.type-description");
        violations[0].Message.Should().Contain("Inactive");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // enum.value-description (requires x-enum-descriptions extension)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnumValueDescription_WhenExtensionPresent_NoViolation()
    {
        var doc = BuildEnumDocWithExtension(
            ["Active", "Inactive"],
            ["User is active.", "User is disabled."]);
        var rule = new EnumValueDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void EnumValueDescription_WhenExtensionMissing_OneViolation()
    {
        var doc = BuildEnumDoc("Active", "Inactive", description: "Status.");
        // No x-enum-descriptions extension
        var rule = new EnumValueDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("enum.value-description");
    }

    [Fact]
    public void EnumValueDescription_WhenEmptyEntryInExtension_OneViolation()
    {
        var doc = BuildEnumDocWithExtension(
            ["Active", "Inactive"],
            ["User is active.", ""]); // second entry empty
        var rule = new EnumValueDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("enum.value-description");
        violations[0].Message.Should().Contain("Inactive");
    }

    [Fact]
    public void TryReadEnumDescriptions_WhenJsonArray_ReturnsTrue()
    {
        var arr = new JsonArray("Desc1", "Desc2");
        var ext = new JsonNodeExtension(arr);
        var ok = EnumValueDescriptionRule.TryReadEnumDescriptions(ext, out var descs);
        ok.Should().BeTrue();
        descs.Should().HaveCount(2);
        descs[0].Should().Be("Desc1");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildEnumDoc(string v1, string v2, string? description)
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = description,
                        Enum = MakeEnumValues(v1, v2),
                    }
                }
            }
        };
    }

    private static OpenApiDocument BuildEnumDocWithExtension(string[] values, string[] descriptions)
    {
        var descArr = new JsonArray(descriptions.Select(d => (JsonNode?)JsonValue.Create(d)).ToArray());

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Enum schema",
                        Enum = MakeEnumValues(values),
                        Extensions = new Dictionary<string, IOpenApiExtension>
                        {
                            ["x-enum-descriptions"] = new JsonNodeExtension(descArr),
                        },
                    }
                }
            }
        };
    }
}
