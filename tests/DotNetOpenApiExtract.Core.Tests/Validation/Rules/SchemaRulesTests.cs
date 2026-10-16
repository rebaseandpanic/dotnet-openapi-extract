using RequiredAttribute = System.ComponentModel.DataAnnotations.RequiredAttribute;
using System.Text.Json.Serialization;
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

    /// <summary>The CLR model of <c>RequiredDto</c>: three declarations of a requirement and two members without one.</summary>
    private sealed class RequiredDto
    {
        [Required] public string? Name { get; set; }
        [JsonRequired] public int Count { get; set; }
        public required string Code { get; set; }
        [Required, JsonPropertyName("display_name")] public string? Display { get; set; }
        public int Plain { get; set; }
        public string Text { get; set; } = "";
        [Required] public string? FirstName { get; set; }
        [Required, JsonIgnore] public string? Hidden { get; set; }
        [JsonPropertyName("hidden")] public string? Shown { get; set; }
    }

    private static OpenApiDocument RequiredDocument(params string[] required) => new()
    {
        Info = new OpenApiInfo { Title = "T", Version = "v1" },
        Components = new OpenApiComponents
        {
            Schemas = new Dictionary<string, IOpenApiSchema>
            {
                ["RequiredDto"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["name"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                        ["count"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                        ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["display_name"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                        ["plain"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                        ["text"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        ["first_name"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                        ["hidden"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                    },
                    Required = new HashSet<string>(required),
                },
            },
        },
    };

    private static readonly ValidationContext RequiredContext = new()
    {
        TypeBySchemaId = new Dictionary<string, Type> { ["RequiredDto"] = typeof(RequiredDto) },
    };

    /// <summary>
    /// <c>[Required]</c>, <c>[JsonRequired]</c> and the <c>required</c> modifier declare a requirement the
    /// schema must list; a non-nullable value type or reference type declares none (System.Text.Json
    /// reads a default for an absent <c>int</c>), so <c>plain</c> and <c>text</c> are not reported. The
    /// property is found under a naming policy (<c>first_name</c>), and an ignored member is never the one
    /// written (<c>hidden</c> is <c>Shown</c>, not the <c>[Required]</c> <c>Hidden</c>).
    /// </summary>
    [Fact]
    public void SchemaRequiredConsistency_DeclaredRequiredNotListed_IsReported_AndUndeclaredIsNot()
    {
        var violations = new SchemaRequiredConsistencyRule().Validate(RequiredDocument(), RequiredContext).ToList();

        violations.Select(v => v.JsonPointer).Should().BeEquivalentTo(
        [
            "#/components/schemas/RequiredDto/properties/code",
            "#/components/schemas/RequiredDto/properties/count",
            "#/components/schemas/RequiredDto/properties/display_name",
            "#/components/schemas/RequiredDto/properties/first_name",
            "#/components/schemas/RequiredDto/properties/name",
        ]);
        violations.Should().OnlyContain(v => v.RuleId == "schema.required-consistency");
    }

    [Fact]
    public void SchemaRequiredConsistency_DeclaredRequiredListed_NoViolation()
    {
        new SchemaRequiredConsistencyRule().Validate(RequiredDocument("name", "count", "code", "display_name", "first_name"), RequiredContext)
            .Should().BeEmpty();
    }

    /// <summary>Without CLR bindings a schema does not say what the code declares: nothing is reported.</summary>
    [Fact]
    public void SchemaRequiredConsistency_Standalone_NoViolation()
    {
        new SchemaRequiredConsistencyRule().Validate(RequiredDocument(), DefaultContext).Should().BeEmpty();
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
