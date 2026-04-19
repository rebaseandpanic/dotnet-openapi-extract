using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for Wave 9 rules:
/// R48: operation.has-required-response-codes (off by default, Error)
/// R49: operation.request-body-description (Warning, on by default)
/// R50: operation.operation-id-pascal-case (off by default, Warning)
/// R51: schema.additional-properties-explicit (off by default, Warning)
/// R52: response.content-type-json-default (off by default, Warning)
/// Plus: per-rule min-description-length and EnumValueDescriptionRule enhancement.
/// </summary>
public sealed class Wave9RulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // R48. operation.has-required-response-codes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void R48_NoConfig_NoViolation()
    {
        // Rule emits nothing when RequiredResponseCodes is null
        var doc = MakeDocWithOperation("POST", "/api/orders");
        var rule = new OperationHasRequiredResponseCodesRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R48_RequiredCodePresent_NoViolation()
    {
        var doc = MakeDocWithOperation("POST", "/api/orders", responses: new[] { "200", "422" });
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)> { ("POST", 422) },
            EnabledRuleIds = new HashSet<string> { "operation.has-required-response-codes" },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        rule.Validate(doc, ctx).Should().BeEmpty();
    }

    [Fact]
    public void R48_RequiredCodeMissing_OneViolation()
    {
        var doc = MakeDocWithOperation("POST", "/api/orders", responses: new[] { "200" });
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)> { ("POST", 422) },
            EnabledRuleIds = new HashSet<string> { "operation.has-required-response-codes" },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        var violations = rule.Validate(doc, ctx).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.has-required-response-codes");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
        violations[0].JsonPointer.Should().Contain("422");
    }

    [Fact]
    public void R48_MutatingFilter_AppliesTo_Post_Put_Patch_Delete()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/items"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Post]   = MakeOperation("200"),
                        [HttpMethod.Put]    = MakeOperation("200"),
                        [HttpMethod.Patch]  = MakeOperation("200"),
                        [HttpMethod.Delete] = MakeOperation("200"),
                        [HttpMethod.Get]    = MakeOperation("200"),
                    }
                }
            }
        };
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)> { ("mutating", 422) },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        var violations = rule.Validate(doc, ctx).ToList();

        // GET should NOT be flagged, the 4 mutating methods should be
        violations.Should().HaveCount(4);
        violations.Should().NotContain(v => v.JsonPointer.Contains("/get/"));
        violations.Should().Contain(v => v.JsonPointer.Contains("/post/"));
        violations.Should().Contain(v => v.JsonPointer.Contains("/put/"));
        violations.Should().Contain(v => v.JsonPointer.Contains("/patch/"));
        violations.Should().Contain(v => v.JsonPointer.Contains("/delete/"));
    }

    [Fact]
    public void R48_SafeFilter_AppliesTo_Get_Head_Options()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/items"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get]     = MakeOperation("200"),
                        [HttpMethod.Head]    = MakeOperation("200"),
                        [HttpMethod.Options] = MakeOperation("200"),
                        [HttpMethod.Post]    = MakeOperation("200"),
                    }
                }
            }
        };
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)> { ("safe", 404) },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        var violations = rule.Validate(doc, ctx).ToList();

        // POST should NOT be flagged, the 3 safe methods should be
        violations.Should().HaveCount(3);
        violations.Should().NotContain(v => v.JsonPointer.Contains("/post/"));
    }

    [Fact]
    public void R48_StarFilter_AppliesTo_AllMethods()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/items"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get]  = MakeOperation("200"),
                        [HttpMethod.Post] = MakeOperation("200"),
                    }
                }
            }
        };
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)> { ("*", 401) },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        var violations = rule.Validate(doc, ctx).ToList();
        violations.Should().HaveCount(2);
    }

    [Fact]
    public void R48_MultipleRequiredCodes_AllChecked()
    {
        var doc = MakeDocWithOperation("POST", "/api/orders", responses: new[] { "200" });
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)>
            {
                ("mutating", 422),
                ("*", 401),
            },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        var violations = rule.Validate(doc, ctx).ToList();
        // Both 422 and 401 are missing
        violations.Should().HaveCount(2);
    }

    [Fact]
    public void R48_OverlappingFilters_DeduplicatedToOneViolation()
    {
        // Both ("*", 422) and ("mutating", 422) match POST — should produce exactly 1 violation,
        // not 2 violations with the same JsonPointer.
        var doc = MakeDocWithOperation("POST", "/api/orders", responses: new[] { "200" });
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)>
            {
                ("*", 422),
                ("mutating", 422),
            },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        var violations = rule.Validate(doc, ctx).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.has-required-response-codes");
        violations[0].JsonPointer.Should().Contain("422");
    }

    [Fact]
    public void R48_ExcludedPath_Skipped()
    {
        var doc = MakeDocWithOperation("POST", "/healthz", responses: new[] { "200" });
        var ctx = new ValidationContext
        {
            RequiredResponseCodes = new List<(string, int)> { ("POST", 422) },
            ExcludedPathPrefixes = new List<string> { "/healthz" },
        };
        var rule = new OperationHasRequiredResponseCodesRule();
        rule.Validate(doc, ctx).Should().BeEmpty();
    }

    [Fact]
    public void R48_IsOffByDefault()
    {
        CoreValidator.DefaultOffRuleIds.Should().Contain("operation.has-required-response-codes");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R49. operation.request-body-description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void R49_NoRequestBody_NoViolation()
    {
        var doc = MakeDocWithOperation("GET", "/api/items", responses: new[] { "200" });
        var rule = new OperationRequestBodyDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R49_RequestBodyWithDescription_NoViolation()
    {
        var doc = MakeDocWithOperationAndRequestBody("/api/orders", "Creates a new order in the system.");
        var rule = new OperationRequestBodyDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R49_RequestBodyMissingDescription_OneViolation()
    {
        var doc = MakeDocWithOperationAndRequestBody("/api/orders", null);
        var rule = new OperationRequestBodyDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.request-body-description");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].JsonPointer.Should().Contain("requestBody");
    }

    [Fact]
    public void R49_RequestBodyDescriptionTooShort_OneViolation()
    {
        // Default MinDescriptionLength = 5; "Hi" is 2 chars
        var doc = MakeDocWithOperationAndRequestBody("/api/orders", "Hi");
        var rule = new OperationRequestBodyDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.request-body-description");
    }

    [Fact]
    public void R49_PerRuleMinLength_Override_LongerDescription_Violation()
    {
        // Global min = 5, override for R49 = 20; "Hello" is 5 chars (passes global but fails override)
        var doc = MakeDocWithOperationAndRequestBody("/api/orders", "Hello");
        var ctx = new ValidationContext
        {
            MinDescriptionLengthPerRule = new Dictionary<string, int>
            {
                ["operation.request-body-description"] = 20
            },
        };
        var rule = new OperationRequestBodyDescriptionRule();
        var violations = rule.Validate(doc, ctx).ToList();
        violations.Should().HaveCount(1);
    }

    [Fact]
    public void R49_IsOnByDefault()
    {
        CoreValidator.DefaultOffRuleIds.Should().NotContain("operation.request-body-description");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R50. operation.operation-id-pascal-case
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void R50_PascalCaseOperationId_NoViolation()
    {
        var doc = MakeDocWithOperationId("GetUsers");
        var rule = new OperationOperationIdPascalCaseRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R50_CamelCaseOperationId_OneViolation()
    {
        var doc = MakeDocWithOperationId("getUsers");
        var rule = new OperationOperationIdPascalCaseRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.operation-id-pascal-case");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("getUsers");
    }

    [Fact]
    public void R50_UnderscoreOperationId_OneViolation()
    {
        var doc = MakeDocWithOperationId("Get_Users");
        var rule = new OperationOperationIdPascalCaseRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    [Fact]
    public void R50_HyphenOperationId_OneViolation()
    {
        var doc = MakeDocWithOperationId("Get-Users");
        var rule = new OperationOperationIdPascalCaseRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    [Fact]
    public void R50_NoOperationId_Skipped()
    {
        var doc = MakeDocWithOperationId(null);
        var rule = new OperationOperationIdPascalCaseRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R50_IsOffByDefault()
    {
        CoreValidator.DefaultOffRuleIds.Should().Contain("operation.operation-id-pascal-case");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R51. schema.additional-properties-explicit
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void R51_AdditionalPropertiesSchemaSet_NoViolation()
    {
        // When AdditionalProperties has an explicit schema, it counts as explicit
        var doc = MakeDocWithSchemaExplicitAdditionalProps("UserDto", hasSchema: true, allowedFalse: false);
        var rule = new SchemaAdditionalPropertiesExplicitRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R51_AdditionalPropertiesAllowedFalse_NoViolation()
    {
        // When AdditionalPropertiesAllowed = false, it counts as explicit
        var doc = MakeDocWithSchemaExplicitAdditionalProps("UserDto", hasSchema: false, allowedFalse: true);
        var rule = new SchemaAdditionalPropertiesExplicitRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R51_AdditionalPropertiesNotSet_OneViolation()
    {
        // No schema and AdditionalPropertiesAllowed = default (true) → not explicit → violation
        var doc = MakeDocWithSchemaExplicitAdditionalProps("UserDto", hasSchema: false, allowedFalse: false);
        var rule = new SchemaAdditionalPropertiesExplicitRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.additional-properties-explicit");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].JsonPointer.Should().Contain("UserDto");
    }

    [Fact]
    public void R51_CompositionSchema_Skipped()
    {
        // Schemas with allOf are excluded
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["UserDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
                        },
                        // AdditionalProperties NOT set, but has AllOf — should be skipped
                        AllOf = new List<IOpenApiSchema>
                        {
                            new OpenApiSchema { Type = JsonSchemaType.Object }
                        },
                    }
                }
            }
        };
        var rule = new SchemaAdditionalPropertiesExplicitRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Theory]
    [InlineData("anyOf")]
    [InlineData("oneOf")]
    public void R51_CompositionSchemaAnyOfOneOf_Skipped(string compositionKind)
    {
        // AnyOf and OneOf must also trigger the composition-skip branch in production.
        // Regression guard: dropping either branch from the isComposition check would
        // cause these schemas to produce false violations.
        var properties = new Dictionary<string, IOpenApiSchema>
        {
            ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
        };
        var innerSchema = new List<IOpenApiSchema>
        {
            new OpenApiSchema { Type = JsonSchemaType.Object }
        };

        var schema = new OpenApiSchema
        {
            Type       = JsonSchemaType.Object,
            Properties = properties,
        };

        if (compositionKind == "anyOf")
            schema.AnyOf = innerSchema;
        else
            schema.OneOf = innerSchema;

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema> { ["TestDto"] = schema }
            }
        };

        var rule = new SchemaAdditionalPropertiesExplicitRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty(
            because: $"schemas with {compositionKind} are excluded from the additionalProperties check");
    }

    [Fact]
    public void R51_SchemaWithNoProperties_Skipped()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["EmptyDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        // No properties — should be skipped
                    }
                }
            }
        };
        var rule = new SchemaAdditionalPropertiesExplicitRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R51_IsOffByDefault()
    {
        CoreValidator.DefaultOffRuleIds.Should().Contain("schema.additional-properties-explicit");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R52. response.content-type-json-default
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void R52_ResponseWithJson_NoViolation()
    {
        var doc = MakeDocWithResponseContent("/api/items", "200", "application/json");
        var rule = new ResponseContentTypeJsonDefaultRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R52_ResponseWithBothXmlAndJson_NoViolation()
    {
        var doc = MakeDocWithResponseContent("/api/items", "200", "application/json", "application/xml");
        var rule = new ResponseContentTypeJsonDefaultRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R52_ResponseWithXmlOnly_OneViolation()
    {
        var doc = MakeDocWithResponseContent("/api/items", "200", "application/xml");
        var rule = new ResponseContentTypeJsonDefaultRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("response.content-type-json-default");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("application/xml");
    }

    [Fact]
    public void R52_ResponseWithNoContent_Skipped()
    {
        // Response with no content body is skipped
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/items"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Delete] = new OpenApiOperation
                        {
                            Responses = new OpenApiResponses
                            {
                                ["204"] = new OpenApiResponse { Description = "No content" }
                                // No Content dict → skipped
                            }
                        }
                    }
                }
            }
        };
        var rule = new ResponseContentTypeJsonDefaultRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void R52_IsOffByDefault()
    {
        CoreValidator.DefaultOffRuleIds.Should().Contain("response.content-type-json-default");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-rule min-description-length helper
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetMinDescriptionLength_NoOverride_ReturnsGlobal()
    {
        var ctx = new ValidationContext { MinDescriptionLength = 10 };
        ctx.GetMinDescriptionLength("operation.description").Should().Be(10);
    }

    [Fact]
    public void GetMinDescriptionLength_WithOverride_ReturnsOverride()
    {
        var ctx = new ValidationContext
        {
            MinDescriptionLength = 10,
            MinDescriptionLengthPerRule = new Dictionary<string, int>
            {
                ["operation.description"] = 30
            }
        };
        ctx.GetMinDescriptionLength("operation.description").Should().Be(30);
    }

    [Fact]
    public void GetMinDescriptionLength_OverrideForOtherRule_ReturnsGlobal()
    {
        var ctx = new ValidationContext
        {
            MinDescriptionLength = 10,
            MinDescriptionLengthPerRule = new Dictionary<string, int>
            {
                ["operation.description"] = 30
            }
        };
        ctx.GetMinDescriptionLength("schema.description").Should().Be(10);
    }

    [Fact]
    public void OperationDescriptionRule_PerRuleOverride_GlobalMin5_OverrideMin30_Violation()
    {
        // Description has 6 chars — passes global(5) but fails override(30)
        var doc = MakeDocWithOperationDescription("Six ch");
        var ctx = new ValidationContext
        {
            MinDescriptionLength = 5,
            MinDescriptionLengthPerRule = new Dictionary<string, int>
            {
                ["operation.description"] = 30
            }
        };
        var rule = new OperationDescriptionRule();
        var violations = rule.Validate(doc, ctx).ToList();
        violations.Should().HaveCount(1, because: "description is 6 chars but override requires 30");
    }

    [Fact]
    public void OperationDescriptionRule_PerRuleOverride_GlobalMin30_OverrideMin5_NoViolation()
    {
        // Description has 6 chars — fails global(30) but passes override(5)
        var doc = MakeDocWithOperationDescription("Six ch");
        var ctx = new ValidationContext
        {
            MinDescriptionLength = 30,
            MinDescriptionLengthPerRule = new Dictionary<string, int>
            {
                ["operation.description"] = 5
            }
        };
        var rule = new OperationDescriptionRule();
        rule.Validate(doc, ctx).Should().BeEmpty(because: "override is 5 and description is 6 chars");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EnumValueDescriptionRule enhancement — min-length support
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EnumValueDescriptionRule_EmptyDescription_ViolationAsAlways()
    {
        var doc = MakeDocWithEnumSchema("Status", new[] { "Active" }, new[] { "" });
        var rule = new EnumValueDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    [Fact]
    public void EnumValueDescriptionRule_DescriptionMeetsDefault_NoViolation()
    {
        // Default MinDescriptionLength = 5; "Active state" is 12 chars
        var doc = MakeDocWithEnumSchema("Status", new[] { "Active" }, new[] { "Active state" });
        var rule = new EnumValueDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void EnumValueDescriptionRule_DescriptionBelowDefault_Violation()
    {
        // Default MinDescriptionLength = 5; "OK" is 2 chars
        var doc = MakeDocWithEnumSchema("Status", new[] { "Active" }, new[] { "OK" });
        var rule = new EnumValueDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    [Fact]
    public void EnumValueDescriptionRule_PerRuleOverride_PassesWithShortDescription()
    {
        // Global = 5, override for enum.value-description = 3; "OK" is 2 chars → still fails
        // But "Yes" is 3 chars → passes override
        var doc = MakeDocWithEnumSchema("Status", new[] { "Active" }, new[] { "Yes" });
        var ctx = new ValidationContext
        {
            MinDescriptionLength = 5,
            MinDescriptionLengthPerRule = new Dictionary<string, int>
            {
                ["enum.value-description"] = 3
            }
        };
        var rule = new EnumValueDescriptionRule();
        rule.Validate(doc, ctx).Should().BeEmpty(because: "'Yes' is 3 chars and override is 3");
    }

    [Fact]
    public void EnumValueDescriptionRule_PerRuleOverride_FailsWithTooShort()
    {
        // Global = 5, override for enum.value-description = 15; "Active" is 6 chars → fails
        var doc = MakeDocWithEnumSchema("Status", new[] { "Active" }, new[] { "Active" });
        var ctx = new ValidationContext
        {
            MinDescriptionLength = 5,
            MinDescriptionLengthPerRule = new Dictionary<string, int>
            {
                ["enum.value-description"] = 15
            }
        };
        var rule = new EnumValueDescriptionRule();
        rule.Validate(doc, ctx).Should().HaveCount(1,
            because: "'Active' is 6 chars but override requires 15");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Wave 9 overall counts
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllRuleIds_Count_Is52()
    {
        CoreValidator.AllRuleIds.Should().HaveCount(52);
    }

    [Fact]
    public void DefaultOffRuleIds_Count_Is9()
    {
        CoreValidator.DefaultOffRuleIds.Should().HaveCount(9);
    }

    [Fact]
    public void Wave9Rules_AreInAllRules()
    {
        CoreValidator.AllRuleIds.Should().Contain("operation.has-required-response-codes");
        CoreValidator.AllRuleIds.Should().Contain("operation.request-body-description");
        CoreValidator.AllRuleIds.Should().Contain("operation.operation-id-pascal-case");
        CoreValidator.AllRuleIds.Should().Contain("schema.additional-properties-explicit");
        CoreValidator.AllRuleIds.Should().Contain("response.content-type-json-default");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R48 via OpenApiValidator.Validate (full pipeline)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void R48_FullPipeline_EnabledWithConfig_ProducesViolation()
    {
        var doc = MakeDocWithOperation("POST", "/api/orders", responses: new[] { "200" });
        var ctx = new ValidationContext
        {
            EnabledRuleIds = new HashSet<string> { "operation.has-required-response-codes" },
            RequiredResponseCodes = new List<(string, int)> { ("mutating", 422) },
        };
        var result = CoreValidator.Validate(doc, ctx);
        result.Violations.Should().Contain(v => v.RuleId == "operation.has-required-response-codes");
    }

    [Fact]
    public void R48_FullPipeline_NotEnabled_NeverFires()
    {
        var doc = MakeDocWithOperation("POST", "/api/orders", responses: new[] { "200" });
        var ctx = new ValidationContext
        {
            // R48 is off by default — not enabled
            RequiredResponseCodes = new List<(string, int)> { ("mutating", 422) },
        };
        var result = CoreValidator.Validate(doc, ctx);
        result.Violations.Should().NotContain(v => v.RuleId == "operation.has-required-response-codes");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper methods
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument MakeDocWithOperation(
        string method, string path, string[]? responses = null)
    {
        var httpMethod = method.ToUpperInvariant() switch
        {
            "GET"    => HttpMethod.Get,
            "POST"   => HttpMethod.Post,
            "PUT"    => HttpMethod.Put,
            "PATCH"  => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            _        => HttpMethod.Get,
        };

        var responsesDict = new OpenApiResponses();
        foreach (var code in responses ?? new[] { "200" })
            responsesDict[code] = new OpenApiResponse { Description = "Response" };

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                [path] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [httpMethod] = new OpenApiOperation { Responses = responsesDict }
                    }
                }
            }
        };
    }

    private static OpenApiOperation MakeOperation(params string[] responseCodes)
    {
        var responses = new OpenApiResponses();
        foreach (var code in responseCodes)
            responses[code] = new OpenApiResponse { Description = "Response" };
        return new OpenApiOperation { Responses = responses };
    }

    private static OpenApiDocument MakeDocWithOperationAndRequestBody(string path, string? description)
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                [path] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Post] = new OpenApiOperation
                        {
                            RequestBody = new OpenApiRequestBody
                            {
                                Description = description,
                                Content = new Dictionary<string, IOpenApiMediaType>
                                {
                                    ["application/json"] = new OpenApiMediaType()
                                }
                            },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "OK" }
                            }
                        }
                    }
                }
            }
        };
    }

    private static OpenApiDocument MakeDocWithOperationId(string? operationId)
    {
        return new OpenApiDocument
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
                            OperationId = operationId,
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "OK" }
                            }
                        }
                    }
                }
            }
        };
    }

    private static OpenApiDocument MakeDocWithSchemaExplicitAdditionalProps(
        string schemaId,
        bool hasSchema,
        bool allowedFalse)
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
            },
        };

        if (hasSchema)
            schema.AdditionalProperties = new OpenApiSchema();
        if (allowedFalse)
            schema.AdditionalPropertiesAllowed = false;

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    [schemaId] = schema
                }
            }
        };
    }

    private static OpenApiDocument MakeDocWithResponseContent(
        string path, string statusCode, params string[] contentTypes)
    {
        var content = new Dictionary<string, IOpenApiMediaType>();
        foreach (var ct in contentTypes)
            content[ct] = new OpenApiMediaType();

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                [path] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            Responses = new OpenApiResponses
                            {
                                [statusCode] = new OpenApiResponse
                                {
                                    Description = "Response",
                                    Content = content
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    private static OpenApiDocument MakeDocWithOperationDescription(string description)
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/items"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            Description = description,
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "OK" }
                            }
                        }
                    }
                }
            }
        };
    }

    private static OpenApiDocument MakeDocWithEnumSchema(
        string schemaId, string[] enumValues, string[] descriptions)
    {
        var extensionNode = new System.Text.Json.Nodes.JsonArray(
            descriptions.Select(d => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(d)).ToArray());

        var schema = new OpenApiSchema
        {
            Enum = enumValues
                .Select(v => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(v)!)
                .ToList(),
            Extensions = new Dictionary<string, IOpenApiExtension>
            {
                ["x-enum-descriptions"] = new Microsoft.OpenApi.JsonNodeExtension(extensionNode)
            }
        };

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    [schemaId] = schema
                }
            }
        };
    }
}
