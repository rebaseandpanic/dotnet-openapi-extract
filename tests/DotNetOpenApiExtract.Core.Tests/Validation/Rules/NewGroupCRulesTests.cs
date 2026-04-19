using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for Wave 7b Group C — developer experience rules (warning severity, off by default).
/// Tests: C1 spec.servers-defined, C2 tag.description, C3 component.no-unused,
///        C4 spec.no-eval-in-markdown, C5 spec.no-script-tags-in-markdown.
/// Also covers: DefaultOffRuleIds list, opt-in mechanism via EnabledRuleIds.
/// </summary>
public sealed class NewGroupCRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // C1. spec.servers-defined
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpecServersDefined_ServersPresent_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Servers = new List<OpenApiServer> { new OpenApiServer { Url = "https://api.example.com" } }
        };
        var rule = new SpecServersDefinedRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SpecServersDefined_NoServers_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
        };
        var rule = new SpecServersDefinedRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("spec.servers-defined");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void SpecServersDefined_EmptyList_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Servers = new List<OpenApiServer>()
        };
        var rule = new SpecServersDefinedRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // C2. tag.description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TagDescription_AllTagsHaveDescriptions_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Tags = new HashSet<OpenApiTag>
            {
                new OpenApiTag { Name = "Users", Description = "Operations related to user management." },
            }
        };
        var rule = new TagDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void TagDescription_MissingDescription_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Tags = new HashSet<OpenApiTag>
            {
                new OpenApiTag { Name = "Users", Description = null },
            }
        };
        var rule = new TagDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("tag.description");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("Users");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // C3. component.no-unused
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComponentNoUnused_UsedSchema_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/users"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUsers",
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Description = "OK",
                                    Content = new Dictionary<string, IOpenApiMediaType>
                                    {
                                        ["application/json"] = new OpenApiMediaType
                                        {
                                            Schema = new OpenApiSchemaReference("UserDto", null, null)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["UserDto"] = new OpenApiSchema { Type = JsonSchemaType.Object }
                }
            }
        };
        var rule = new ComponentNoUnusedRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ComponentNoUnused_UnusedSchema_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            // No paths referencing UserDto
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["UserDto"] = new OpenApiSchema { Type = JsonSchemaType.Object }
                }
            }
        };
        var rule = new ComponentNoUnusedRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("component.no-unused");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("UserDto");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // C4. spec.no-eval-in-markdown
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpecNoEvalInMarkdown_NoEval_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1", Description = "This is a safe description." }
        };
        var rule = new SpecNoEvalInMarkdownRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SpecNoEvalInMarkdown_EvalInDescription_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1", Description = "Run eval(alert('xss')) here." }
        };
        var rule = new SpecNoEvalInMarkdownRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("spec.no-eval-in-markdown");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void SpecNoEvalInMarkdown_EvalCaseInsensitive_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1", Description = "EVAL(badcode)" }
        };
        var rule = new SpecNoEvalInMarkdownRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // C5. spec.no-script-tags-in-markdown
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpecNoScriptTagsInMarkdown_NoScript_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1", Description = "A safe <b>description</b>." }
        };
        var rule = new SpecNoScriptTagsInMarkdownRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SpecNoScriptTagsInMarkdown_ScriptInDescription_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1", Description = "<script>alert('xss')</script>" }
        };
        var rule = new SpecNoScriptTagsInMarkdownRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("spec.no-script-tags-in-markdown");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void SpecNoScriptTagsInMarkdown_ScriptCaseInsensitive_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1", Description = "<SCRIPT>evil()</SCRIPT>" }
        };
        var rule = new SpecNoScriptTagsInMarkdownRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DefaultOffRuleIds — opt-in mechanism
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultOffRuleIds_ContainsExactlyFiveGroupCRules()
    {
        CoreValidator.DefaultOffRuleIds.Should().HaveCount(5);
        CoreValidator.DefaultOffRuleIds.Should().Contain("spec.servers-defined");
        CoreValidator.DefaultOffRuleIds.Should().Contain("tag.description");
        CoreValidator.DefaultOffRuleIds.Should().Contain("component.no-unused");
        CoreValidator.DefaultOffRuleIds.Should().Contain("spec.no-eval-in-markdown");
        CoreValidator.DefaultOffRuleIds.Should().Contain("spec.no-script-tags-in-markdown");
    }

    [Fact]
    public void AllRules_Count_Is47()
    {
        CoreValidator.AllRules.Should().HaveCount(47,
            because: "Wave 7b adds 23 rules to the original 24, for a total of 47");
    }

    [Fact]
    public void AllRuleIds_AreUnique()
    {
        var ids = CoreValidator.AllRuleIds.ToList();
        var duplicates = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        duplicates.Should().BeEmpty(because: "rule IDs should be unique; found duplicates: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void GroupCRules_NotRun_WithDefaultContext()
    {
        // A document that violates all Group C rules
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "API",
                Version = "v1",
                Description = "This is an eval(dangerous) description with <script>evil</script>"
            },
            // No servers, no tags → violations for servers-defined and tag.description
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Unused"] = new OpenApiSchema { Type = JsonSchemaType.Object }
                }
            }
        };

        var ctx = new ValidationContext(); // default — no EnabledRuleIds
        var result = CoreValidator.Validate(doc, ctx);

        // Group C rules must NOT fire with default context
        var groupCIds = CoreValidator.DefaultOffRuleIds;
        result.Violations.Should().NotContain(v => groupCIds.Contains(v.RuleId),
            because: "Group C rules are off by default");
    }

    [Fact]
    public void GroupCRules_Run_WhenEnabledViaEnabledRuleIds()
    {
        // A document that violates spec.servers-defined
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            // No servers
        };

        var ctx = new ValidationContext
        {
            EnabledRuleIds = new HashSet<string> { "spec.servers-defined" }
        };

        var result = CoreValidator.Validate(doc, ctx);

        result.Violations.Should().Contain(v => v.RuleId == "spec.servers-defined",
            because: "spec.servers-defined was explicitly enabled");
    }

    [Fact]
    public void GroupCRules_OnlyEnabled_OthersStillOff()
    {
        // Enable only one Group C rule — others should still not fire
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "API",
                Version = "v1",
                Description = "This description contains eval(xss) and <script>evil</script>"
            },
        };

        var ctx = new ValidationContext
        {
            EnabledRuleIds = new HashSet<string> { "spec.no-eval-in-markdown" }
            // spec.no-script-tags-in-markdown NOT enabled
        };

        var result = CoreValidator.Validate(doc, ctx);

        result.Violations.Should().Contain(v => v.RuleId == "spec.no-eval-in-markdown",
            because: "spec.no-eval-in-markdown was enabled");
        result.Violations.Should().NotContain(v => v.RuleId == "spec.no-script-tags-in-markdown",
            because: "spec.no-script-tags-in-markdown was not enabled");
    }

    [Fact]
    public void EnabledRuleIds_DefaultContext_IsEmpty()
    {
        var ctx = new ValidationContext();
        ctx.EnabledRuleIds.Should().BeEmpty(
            because: "default context has no enabled rules (Group C rules require explicit opt-in)");
    }

    [Fact]
    public void SkipRule_WinsOverEnabledRule_RuleDoesNotFire()
    {
        // A document that would violate spec.servers-defined (no servers)
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
        };

        // Both enable and skip the same Group C rule.
        // OpenApiValidator checks SkippedRuleIds BEFORE the EnabledRuleIds gate,
        // so skip must win and the rule must not fire.
        var ctx = new ValidationContext
        {
            SkippedRuleIds = new HashSet<string> { "spec.servers-defined" },
            EnabledRuleIds = new HashSet<string> { "spec.servers-defined" }
        };

        var result = CoreValidator.Validate(doc, ctx);

        result.Violations.Should().NotContain(v => v.RuleId == "spec.servers-defined",
            because: "SkippedRuleIds is evaluated before EnabledRuleIds — skip must win over enable");
    }
}
