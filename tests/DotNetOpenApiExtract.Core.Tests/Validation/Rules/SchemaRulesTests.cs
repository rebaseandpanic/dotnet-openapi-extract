using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for schema-level validation rules.
/// </summary>
public sealed class SchemaRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // schema.description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaDescription_WhenPresent_NoViolation()
    {
        var doc = BuildDocWithSchema(s =>
        {
            s.Type = JsonSchemaType.Object;
            s.Description = "A user data transfer object";
        });
        var rule = new SchemaDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaDescription_WhenMissing_OneViolation()
    {
        var doc = BuildDocWithSchema(s =>
        {
            s.Type = JsonSchemaType.Object;
            s.Description = null;
        });
        var rule = new SchemaDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.description");
    }

    [Fact]
    public void SchemaDescription_WhenPrimitive_NoViolation()
    {
        // String schema is not an object — should not be flagged
        var doc = BuildDocWithSchema(s =>
        {
            s.Type = JsonSchemaType.String;
            s.Description = null;
        });
        var rule = new SchemaDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // schema.property-description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaPropertyDescription_WhenPresent_NoViolation()
    {
        var doc = BuildDocWithSchemaProps(props =>
        {
            props["email"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = "User email address",
            };
        });
        var rule = new SchemaPropertyDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaPropertyDescription_WhenMissing_OneViolation()
    {
        var doc = BuildDocWithSchemaProps(props =>
        {
            props["email"] = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Description = null,
            };
        });
        var rule = new SchemaPropertyDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.property-description");
        violations[0].JsonPointer.Should().Contain("email");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // schema.required-consistency
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaRequiredConsistency_WhenValueTypeInRequired_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["TestDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["count"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
                        },
                        Required = new HashSet<string> { "count" },
                    }
                }
            }
        };
        var rule = new SchemaRequiredConsistencyRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaRequiredConsistency_WhenValueTypeNotInRequired_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["TestDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["count"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
                        },
                        Required = new HashSet<string>(),
                    }
                }
            }
        };
        var rule = new SchemaRequiredConsistencyRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.required-consistency");
    }

    // Fix I2: non-nullable string (NRT, type=String only) must also be in required
    [Fact]
    public void SchemaRequiredConsistency_WhenNonNullableStringNotInRequired_OneViolation()
    {
        // String with type=String only (no Null flag) = NRT non-nullable → must be in required.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["TestDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["name"] = new OpenApiSchema { Type = JsonSchemaType.String }
                        },
                        Required = new HashSet<string>(),
                    }
                }
            }
        };
        var rule = new SchemaRequiredConsistencyRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.required-consistency");
        violations[0].JsonPointer.Should().Contain("name");
    }

    [Fact]
    public void SchemaRequiredConsistency_WhenNullableStringNotInRequired_NoViolation()
    {
        // String | Null = nullable reference type → allowed to be absent from required.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["TestDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["name"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null }
                        },
                        Required = new HashSet<string>(),
                    }
                }
            }
        };
        var rule = new SchemaRequiredConsistencyRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaRequiredConsistency_WhenNonNullableStringInRequired_NoViolation()
    {
        // Non-nullable string that IS listed in required → no violation.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["TestDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["name"] = new OpenApiSchema { Type = JsonSchemaType.String }
                        },
                        Required = new HashSet<string> { "name" },
                    }
                }
            }
        };
        var rule = new SchemaRequiredConsistencyRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // schema.enum-filled
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchemaEnumFilled_WhenEnumHasValues_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = new List<System.Text.Json.Nodes.JsonNode>
                        {
                            System.Text.Json.Nodes.JsonValue.Create("Active")!,
                            System.Text.Json.Nodes.JsonValue.Create("Inactive")!,
                        }
                    }
                }
            }
        };
        var rule = new SchemaEnumFilledRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SchemaEnumFilled_WhenEnumEmpty_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Enum = new List<System.Text.Json.Nodes.JsonNode>(),
                    }
                }
            }
        };
        var rule = new SchemaEnumFilledRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("schema.enum-filled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocWithSchema(Action<OpenApiSchema> configure)
    {
        var schema = new OpenApiSchema();
        configure(schema);

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["TestDto"] = schema
                }
            }
        };
    }

    private static OpenApiDocument BuildDocWithSchemaProps(Action<Dictionary<string, IOpenApiSchema>> configure)
    {
        var props = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        configure(props);

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["TestDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Description = "Test schema",
                        Properties = props,
                    }
                }
            }
        };
    }
}
