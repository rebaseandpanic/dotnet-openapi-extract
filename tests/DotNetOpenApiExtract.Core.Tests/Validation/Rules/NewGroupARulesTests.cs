using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for Wave 7b Group A — spec-MUST violation rules (error severity).
/// Tests: A0 spec.no-ref-siblings, A1 spec.info-version, A2 operation.operation-id-unique,
///        A3 path.params-match, A4 path.no-empty-declaration, A5 parameter.path-required-true,
///        A6 schema.array-items, A7 operation.parameters-unique.
/// </summary>
public sealed class NewGroupARulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // A0. spec.no-ref-siblings
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpecNoRefSiblings_NoRefs_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["MySchema"] = new OpenApiSchema { Type = JsonSchemaType.Object }
                }
            }
        };
        var rule = new SpecNoRefSiblingsRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SpecNoRefSiblings_RefWithDescription_OneViolation()
    {
        // An OpenApiSchemaReference with Description set is a sibling property in OAS 3.0
        var refWithSibling = new OpenApiSchemaReference("OtherSchema", null, null);
        refWithSibling.Description = "This description is a sibling of $ref in OAS 3.0";

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["MySchema"] = refWithSibling,
                    ["OtherSchema"] = new OpenApiSchema { Type = JsonSchemaType.Object },
                }
            }
        };
        var rule = new SpecNoRefSiblingsRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("spec.no-ref-siblings");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
    }

    [Fact]
    public void SpecNoRefSiblings_OpenApi31_SkipsRule()
    {
        // In OpenAPI 3.1 $ref siblings are legal per JSON Schema Draft 2020-12;
        // the rule should emit no violations.
        var refWithSibling = new OpenApiSchemaReference("OtherSchema", null, null);
        refWithSibling.Description = "This is a sibling; legal in 3.1+";

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["MySchema"] = refWithSibling,
                    ["OtherSchema"] = new OpenApiSchema { Type = JsonSchemaType.Object },
                }
            }
        };
        var context = new ValidationContext
        {
            OpenApiSpecVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1
        };
        var rule = new SpecNoRefSiblingsRule();
        rule.Validate(doc, context).Should().BeEmpty(
            because: "spec.no-ref-siblings does not apply to OpenAPI 3.1");
    }

    [Fact]
    public void SpecNoRefSiblings_OpenApi32_SkipsRule()
    {
        // Same as 3.1 — $ref siblings are legal in 3.2.
        var refWithSibling = new OpenApiSchemaReference("OtherSchema", null, null);
        refWithSibling.Description = "Legal sibling in 3.2";

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["MySchema"] = refWithSibling,
                    ["OtherSchema"] = new OpenApiSchema { Type = JsonSchemaType.Object },
                }
            }
        };
        var context = new ValidationContext
        {
            OpenApiSpecVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_2
        };
        var rule = new SpecNoRefSiblingsRule();
        rule.Validate(doc, context).Should().BeEmpty(
            because: "spec.no-ref-siblings does not apply to OpenAPI 3.2");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A1. spec.info-version
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpecInfoVersion_WhenPresent_NoViolation()
    {
        var doc = new OpenApiDocument { Info = new OpenApiInfo { Title = "API", Version = "v1" } };
        var rule = new SpecInfoVersionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SpecInfoVersion_WhenMissing_OneViolation()
    {
        var doc = new OpenApiDocument { Info = new OpenApiInfo { Title = "API", Version = null } };
        var rule = new SpecInfoVersionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("spec.info-version");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
        violations[0].JsonPointer.Should().Be("#/info/version");
    }

    [Fact]
    public void SpecInfoVersion_WhenWhitespace_OneViolation()
    {
        var doc = new OpenApiDocument { Info = new OpenApiInfo { Title = "API", Version = "   " } };
        var rule = new SpecInfoVersionRule();
        rule.Validate(doc, DefaultContext).Should().HaveCount(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A2. operation.operation-id-unique
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationOperationIdUnique_AllUnique_NoViolation()
    {
        var doc = BuildDocWithTwoOperations("GetUsers", "CreateUser");
        var rule = new OperationOperationIdUniqueRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationOperationIdUnique_Duplicate_OneViolation()
    {
        var doc = BuildDocWithTwoOperations("GetUsers", "GetUsers");
        var rule = new OperationOperationIdUniqueRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.operation-id-unique");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
        violations[0].Message.Should().Contain("GetUsers");
    }

    [Fact]
    public void OperationOperationIdUnique_ThreeDuplicates_TwoViolations()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/a"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation { OperationId = "Dup" },
                    }
                },
                ["/b"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation { OperationId = "Dup" },
                    }
                },
                ["/c"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation { OperationId = "Dup" },
                    }
                },
            }
        };
        var rule = new OperationOperationIdUniqueRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(2, because: "two extra occurrences beyond the first are flagged");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A3. path.params-match
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PathParamsMatch_AllMatch_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/users/{id}"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUser",
                            Parameters = new List<IOpenApiParameter>
                            {
                                new OpenApiParameter
                                {
                                    Name = "id",
                                    In = ParameterLocation.Path,
                                    Required = true,
                                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                                }
                            },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new PathParamsMatchRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void PathParamsMatch_MissingParameter_OneViolation()
    {
        // Template has {id} but operation has no in:path parameter
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/users/{id}"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUser",
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new PathParamsMatchRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().Contain(v => v.RuleId == "path.params-match" && v.Message.Contains("{id}"));
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
    }

    [Fact]
    public void PathParamsMatch_ExtraPathParam_OneViolation()
    {
        // Operation declares in:path "extra" but template has no {extra}
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/users/{id}"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUser",
                            Parameters = new List<IOpenApiParameter>
                            {
                                new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true },
                                new OpenApiParameter { Name = "extra", In = ParameterLocation.Path, Required = true },
                            },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new PathParamsMatchRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().Contain(v => v.RuleId == "path.params-match" && v.Message.Contains("extra"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A4. path.no-empty-declaration
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PathNoEmptyDeclaration_ValidPath_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths { ["/api/users/{id}"] = new OpenApiPathItem() }
        };
        var rule = new PathNoEmptyDeclarationRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void PathNoEmptyDeclaration_EmptyBraces_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths { ["/api/users/{}"] = new OpenApiPathItem() }
        };
        var rule = new PathNoEmptyDeclarationRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("path.no-empty-declaration");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A5. parameter.path-required-true
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParameterPathRequiredTrue_WhenRequired_NoViolation()
    {
        var doc = BuildDocWithPathParam(required: true);
        var rule = new ParameterPathRequiredTrueRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ParameterPathRequiredTrue_WhenNotRequired_OneViolation()
    {
        var doc = BuildDocWithPathParam(required: false);
        var rule = new ParameterPathRequiredTrueRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("parameter.path-required-true");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A6. schema.array-items
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaArrayItems_WhenItemsDefined_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["MyList"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema { Type = JsonSchemaType.String }
                    }
                }
            }
        };
        var rule = new SchemaArrayItemsRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaArrayItems_WhenItemsMissing_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["MyList"] = new OpenApiSchema { Type = JsonSchemaType.Array }
                }
            }
        };
        var rule = new SchemaArrayItemsRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.array-items");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
    }

    [Fact]
    public void SchemaArrayItems_NestedArrayWithoutItems_OneViolation()
    {
        // Property of type array without items inside an object schema
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["MyDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["items"] = new OpenApiSchema { Type = JsonSchemaType.Array }
                            // No Items defined
                        }
                    }
                }
            }
        };
        var rule = new SchemaArrayItemsRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.array-items");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A7. operation.parameters-unique
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationParametersUnique_AllUnique_NoViolation()
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
                            Parameters = new List<IOpenApiParameter>
                            {
                                new OpenApiParameter { Name = "page", In = ParameterLocation.Query },
                                new OpenApiParameter { Name = "size", In = ParameterLocation.Query },
                            },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new OperationParametersUniqueRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationParametersUnique_DuplicateParam_OneViolation()
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
                            Parameters = new List<IOpenApiParameter>
                            {
                                new OpenApiParameter { Name = "page", In = ParameterLocation.Query },
                                new OpenApiParameter { Name = "page", In = ParameterLocation.Query }, // duplicate
                            },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new OperationParametersUniqueRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.parameters-unique");
        violations[0].Severity.Should().Be(ValidationSeverity.Error);
        violations[0].Message.Should().Contain("page");
    }

    [Fact]
    public void SchemaArrayItems_InlineResponseArrayWithoutItems_OneViolation()
    {
        // Inline array schema in an operation response (not in components) should be flagged
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
                                            Schema = new OpenApiSchema { Type = JsonSchemaType.Array }
                                            // No Items defined
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var rule = new SchemaArrayItemsRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.array-items");
        violations[0].JsonPointer.Should().Contain("responses");
    }

    [Fact]
    public void SchemaArrayItems_InlineRequestBodyArrayWithoutItems_OneViolation()
    {
        // Inline array schema in operation request body should be flagged
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/users"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Post] = new OpenApiOperation
                        {
                            OperationId = "CreateUsers",
                            RequestBody = new OpenApiRequestBody
                            {
                                Content = new Dictionary<string, IOpenApiMediaType>
                                {
                                    ["application/json"] = new OpenApiMediaType
                                    {
                                        Schema = new OpenApiSchema { Type = JsonSchemaType.Array }
                                        // No Items defined
                                    }
                                }
                            },
                            Responses = new OpenApiResponses { ["201"] = new OpenApiResponse { Description = "Created" } }
                        }
                    }
                }
            }
        };
        var rule = new SchemaArrayItemsRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.array-items");
        violations[0].JsonPointer.Should().Contain("requestBody");
    }

    [Fact]
    public void SchemaArrayItems_ComponentPropertyArrayWithoutItems_OneViolation()
    {
        // A property of type array without items inside a component schema (nested recursion)
        // is already tested by SchemaArrayItems_NestedArrayWithoutItems_OneViolation.
        // This variant verifies the inline operation path also catches nested properties.
        var doc = new OpenApiDocument
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
                            OperationId = "GetItems",
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Description = "OK",
                                    Content = new Dictionary<string, IOpenApiMediaType>
                                    {
                                        ["application/json"] = new OpenApiMediaType
                                        {
                                            Schema = new OpenApiSchema
                                            {
                                                Type = JsonSchemaType.Object,
                                                Properties = new Dictionary<string, IOpenApiSchema>
                                                {
                                                    ["tags"] = new OpenApiSchema { Type = JsonSchemaType.Array }
                                                    // No Items defined on nested array property
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
        var rule = new SchemaArrayItemsRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.array-items");
        violations[0].JsonPointer.Should().Contain("properties");
    }

    [Fact]
    public void OperationParametersUnique_PathLevelDuplicatesOperationLevel_OneViolation()
    {
        // A path-level parameter with the same (name, in) as an operation-level parameter
        // is flagged as a duplicate (matches linter strictness over raw OAS spec).
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/users/{id}"] = new OpenApiPathItem
                {
                    Parameters = new List<IOpenApiParameter>
                    {
                        new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true },
                    },
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUser",
                            Parameters = new List<IOpenApiParameter>
                            {
                                new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true }, // same as path-level
                            },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
        var rule = new OperationParametersUniqueRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.parameters-unique");
        violations[0].Message.Should().Contain("id");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocWithTwoOperations(string opId1, string opId2)
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/a"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = opId1,
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                },
                ["/api/b"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = opId2,
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                },
            }
        };
    }

    private static OpenApiDocument BuildDocWithPathParam(bool required)
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "API", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/users/{id}"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            OperationId = "GetUser",
                            Parameters = new List<IOpenApiParameter>
                            {
                                new OpenApiParameter
                                {
                                    Name = "id",
                                    In = ParameterLocation.Path,
                                    Required = required,
                                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                                }
                            },
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
                        }
                    }
                }
            }
        };
    }
}
