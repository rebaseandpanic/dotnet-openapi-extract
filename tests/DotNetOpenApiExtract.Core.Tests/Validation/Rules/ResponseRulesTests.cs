using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for response-level validation rules.
/// </summary>
public sealed class ResponseRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // response.description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResponseDescription_WhenPresent_NoViolation()
    {
        var doc = BuildDocWithResponse("200", r => r.Description = "OK");
        var rule = new ResponseDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ResponseDescription_WhenMissing_OneViolation()
    {
        var doc = BuildDocWithResponse("200", r => r.Description = null);
        var rule = new ResponseDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("response.description");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // response.schema-when-body
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResponseSchemaWhenBody_WhenSchemaPresent_NoViolation()
    {
        var doc = BuildDocWithResponse("200", r =>
        {
            r.Description = "OK";
            r.Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Object }
                }
            };
        });
        var rule = new ResponseSchemaWhenBodyRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void ResponseSchemaWhenBody_WhenContentButNoSchema_OneViolation()
    {
        var doc = BuildDocWithResponse("200", r =>
        {
            r.Description = "OK";
            r.Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = null }
            };
        });
        var rule = new ResponseSchemaWhenBodyRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("response.schema-when-body");
    }

    [Fact]
    public void ResponseSchemaWhenBody_WhenNoContent_NoViolation()
    {
        var doc = BuildDocWithResponse("204", r =>
        {
            r.Description = "No Content";
            r.Content = null;
        });
        var rule = new ResponseSchemaWhenBodyRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocWithResponse(string statusCode, Action<OpenApiResponse> configure)
    {
        var response = new OpenApiResponse();
        configure(response);

        var operation = new OpenApiOperation
        {
            Summary = "Test",
            OperationId = "TestOp",
            Description = "Test description that is long enough for rules.",
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Test", null) },
            Responses = new OpenApiResponses
            {
                [statusCode] = response,
                ["422"] = new OpenApiResponse { Description = "Error" },
            },
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
