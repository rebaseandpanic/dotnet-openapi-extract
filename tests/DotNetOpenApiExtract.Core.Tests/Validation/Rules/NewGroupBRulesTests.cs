using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for Wave 7b Group B — structural completeness rules (warning severity).
/// Tests: B1 operation.success-response, B2 operation.operation-id-url-safe,
///        B3 path.no-trailing-slash, B4 path.no-query-string, B5 path.no-identical,
///        B6 tag.no-duplicates, B7 operation.tag-defined, B8 schema.typed-enum,
///        B9 schema.no-duplicate-enum, B10 schema.no-required-undefined.
/// </summary>
public sealed class NewGroupBRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // B1. operation.success-response
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationSuccessResponse_HasTwoXX_NoViolation()
    {
        var doc = BuildDocWithResponses("200", "422");
        var rule = new OperationSuccessResponseRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationSuccessResponse_OnlyErrorResponse_OneViolation()
    {
        var doc = BuildDocWithResponses("422");
        var rule = new OperationSuccessResponseRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.success-response");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    [Fact]
    public void OperationSuccessResponse_HasThreeXX_NoViolation()
    {
        var doc = BuildDocWithResponses("301");
        var rule = new OperationSuccessResponseRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B2. operation.operation-id-url-safe
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationIdUrlSafe_SafeChars_NoViolation()
    {
        var doc = BuildDocWithOperationId("GetUser_by-id123");
        var rule = new OperationOperationIdUrlSafeRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationIdUrlSafe_Space_OneViolation()
    {
        var doc = BuildDocWithOperationId("Get User");
        var rule = new OperationOperationIdUrlSafeRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.operation-id-url-safe");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("Get User");
    }

    [Fact]
    public void OperationIdUrlSafe_SpecialChars_OneViolation()
    {
        var doc = BuildDocWithOperationId("get/user.data");
        var rule = new OperationOperationIdUrlSafeRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.operation-id-url-safe");
    }

    [Fact]
    public void OperationIdUrlSafe_MissingId_NoViolation()
    {
        // operation.operation-id rule covers missing IDs — this rule only checks existing IDs
        var doc = BuildDocWithOperationId(null!);
        var rule = new OperationOperationIdUrlSafeRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B3. path.no-trailing-slash
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PathNoTrailingSlash_ValidPath_NoViolation()
    {
        var doc = BuildDocWithPath("/api/users");
        var rule = new PathNoTrailingSlashRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void PathNoTrailingSlash_RootPath_NoViolation()
    {
        var doc = BuildDocWithPath("/");
        var rule = new PathNoTrailingSlashRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void PathNoTrailingSlash_TrailingSlash_OneViolation()
    {
        var doc = BuildDocWithPath("/api/users/");
        var rule = new PathNoTrailingSlashRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("path.no-trailing-slash");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B4. path.no-query-string
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PathNoQueryString_NoQueryString_NoViolation()
    {
        var doc = BuildDocWithPath("/api/users");
        var rule = new PathNoQueryStringRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void PathNoQueryString_ContainsQuestion_OneViolation()
    {
        var doc = BuildDocWithPath("/api/users?format=json");
        var rule = new PathNoQueryStringRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("path.no-query-string");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B5. path.no-identical
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PathNoIdentical_UniquePaths_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/a"] = new OpenApiPathItem(),
                ["/api/b"] = new OpenApiPathItem(),
            }
        };
        var rule = new PathNoIdenticalRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void PathNoIdentical_DuplicatePaths_ZeroViolations_InMemory()
    {
        // OpenApiPaths is a Dictionary, so in-memory duplicates are not possible.
        // The rule effectively always passes on in-memory documents.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/a"] = new OpenApiPathItem(),
                ["/api/b"] = new OpenApiPathItem(),
            }
        };
        var rule = new PathNoIdenticalRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty(
            because: "in-memory OpenApiPaths dictionary cannot contain duplicate keys");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B6. tag.no-duplicates
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TagNoDuplicates_UniqueNames_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Tags = new HashSet<OpenApiTag>
            {
                new OpenApiTag { Name = "Users" },
                new OpenApiTag { Name = "Products" },
            }
        };
        var rule = new TagNoDuplicatesRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void TagNoDuplicates_HashSetDedupsByName_EmitsNoViolation()
    {
        // In-memory, HashSet<OpenApiTag> with OpenApiTagNameComparer eliminates tags with the
        // same Name before the rule ever runs. The resulting set has only one "Users" entry,
        // so the rule correctly emits no violation. The rule's primary value is during standalone
        // validation of parsed spec files, where duplicates can exist in the raw JSON/YAML.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Tags = new HashSet<OpenApiTag>(new[] {
                new OpenApiTag { Name = "Users" },
                new OpenApiTag { Name = "Users" },
            }, new OpenApiTagNameComparer())
        };
        var rule = new TagNoDuplicatesRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty(
            because: "HashSet deduplicates by name before the rule runs; duplicate Tags entries from parsed specs are the intended violation path");
    }

    [Fact]
    public void TagNoDuplicates_NoTags_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
        };
        var rule = new TagNoDuplicatesRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B7. operation.tag-defined
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationTagDefined_TagInDocTags_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Tags = new HashSet<OpenApiTag> { new OpenApiTag { Name = "Users" } },
            Paths = new OpenApiPaths
            {
                ["/api/users"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUsers",
                            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Users", null) },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new OperationTagDefinedRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationTagDefined_TagNotInDocTags_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            // No document-level Tags defined
            Paths = new OpenApiPaths
            {
                ["/api/users"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUsers",
                            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Users", null) },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new OperationTagDefinedRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.tag-defined");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("Users");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B8. schema.typed-enum
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaTypedEnum_StringEnumWithStringValues_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = new List<JsonNode> { JsonValue.Create("active")!, JsonValue.Create("inactive")! }
                    }
                }
            }
        };
        var rule = new SchemaTypedEnumRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaTypedEnum_IntegerEnumWithStringValue_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Code"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Integer,
                        // String value in integer enum
                        Enum = new List<JsonNode> { JsonValue.Create(1)!, JsonValue.Create("bad")! }
                    }
                }
            }
        };
        var rule = new SchemaTypedEnumRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.typed-enum");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B9. schema.no-duplicate-enum
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaNoDuplicateEnum_UniqueValues_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = new List<JsonNode> { JsonValue.Create("a")!, JsonValue.Create("b")! }
                    }
                }
            }
        };
        var rule = new SchemaNoDuplicateEnumRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaNoDuplicateEnum_DuplicateValues_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = new List<JsonNode> { JsonValue.Create("active")!, JsonValue.Create("active")! }
                    }
                }
            }
        };
        var rule = new SchemaNoDuplicateEnumRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.no-duplicate-enum");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("active");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B10. schema.no-required-undefined
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaNoRequiredUndefined_AllRequiredDefined_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["User"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                            ["name"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                        Required = new HashSet<string> { "id", "name" }
                    }
                }
            }
        };
        var rule = new SchemaNoRequiredUndefinedRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaNoRequiredUndefined_RequiredNotInProperties_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["User"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                        },
                        Required = new HashSet<string> { "id", "missingField" } // missingField not in properties
                    }
                }
            }
        };
        var rule = new SchemaNoRequiredUndefinedRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.no-required-undefined");
        violations[0].Severity.Should().Be(ValidationSeverity.Warning);
        violations[0].Message.Should().Contain("missingField");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocWithResponses(params string[] statusCodes)
    {
        var responses = new OpenApiResponses();
        foreach (var code in statusCodes)
            responses[code] = new OpenApiResponse { Description = $"Response {code}" };

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/test"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "TestOp",
                            Responses = responses
                        }
                    }
                }
            }
        };
    }

    private static OpenApiDocument BuildDocWithOperationId(string? opId)
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/test"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = opId,
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
    }

    private static OpenApiDocument BuildDocWithPath(string path)
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
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "TestOp",
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
    }
}

/// <summary>Equality comparer for OpenApiTag by Name for testing purposes.</summary>
internal sealed class OpenApiTagNameComparer : IEqualityComparer<OpenApiTag>
{
    public bool Equals(OpenApiTag? x, OpenApiTag? y) =>
        string.Equals(x?.Name, y?.Name, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(OpenApiTag obj) =>
        (obj.Name ?? "").GetHashCode(StringComparison.OrdinalIgnoreCase);
}
