using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for parameter-level validation rules.
/// </summary>
public sealed class ParameterRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // parameter.description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParameterDescription_WhenPresent_NoViolation()
    {
        var doc = BuildDocWithParameter(p => p.Description = "The user identifier");
        var rule = new ParameterDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ParameterDescription_WhenMissing_OneViolation()
    {
        var doc = BuildDocWithParameter(p => p.Description = null);
        var rule = new ParameterDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("parameter.description");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // parameter.schema-type
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParameterSchemaType_WhenTypePresent_NoViolation()
    {
        var doc = BuildDocWithParameter(p =>
        {
            p.Schema = new OpenApiSchema { Type = JsonSchemaType.String };
            p.Description = "A param";
        });
        var rule = new ParameterSchemaTypeRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ParameterSchemaType_WhenSchemaNull_OneViolation()
    {
        var doc = BuildDocWithParameter(p =>
        {
            p.Schema = null;
            p.Description = "A param";
        });
        var rule = new ParameterSchemaTypeRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("parameter.schema-type");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // parameter.optional-has-default
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParameterOptionalHasDefault_WhenIntegerWithDefault_NoViolation()
    {
        var doc = BuildDocWithParameter(p =>
        {
            p.Required = false;
            p.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Default = System.Text.Json.Nodes.JsonValue.Create(1),
            };
            p.Description = "Page number";
        });
        var rule = new ParameterOptionalHasDefaultRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ParameterOptionalHasDefault_WhenIntegerWithoutDefault_OneViolation()
    {
        var doc = BuildDocWithParameter(p =>
        {
            p.Required = false;
            p.Schema = new OpenApiSchema { Type = JsonSchemaType.Integer };
            p.Description = "Page number";
        });
        var rule = new ParameterOptionalHasDefaultRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("parameter.optional-has-default");
    }

    [Fact]
    public void ParameterOptionalHasDefault_WhenStringWithoutDefault_NoViolation()
    {
        // String is a reference type — no default required
        var doc = BuildDocWithParameter(p =>
        {
            p.Required = false;
            p.Schema = new OpenApiSchema { Type = JsonSchemaType.String };
            p.Description = "A filter";
        });
        var rule = new ParameterOptionalHasDefaultRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocWithParameter(Action<OpenApiParameter> configure)
    {
        var param = new OpenApiParameter
        {
            Name = "id",
            In = ParameterLocation.Query,
            Required = true,
            Description = "Parameter description",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };

        configure(param);

        var operation = new OpenApiOperation
        {
            Summary = "Test",
            OperationId = "TestOp",
            Description = "Test description that is long enough for validation rules.",
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Test", null) },
            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } },
            Parameters = new List<IOpenApiParameter> { param },
        };

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/test"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = operation
                    }
                }
            }
        };
    }
}
