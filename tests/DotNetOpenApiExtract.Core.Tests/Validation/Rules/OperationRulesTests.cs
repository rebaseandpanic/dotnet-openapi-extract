using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for operation-level validation rules.
/// Uses minimal in-memory OpenApiDocument objects — does not go through full Build.
/// </summary>
public sealed class OperationRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // operation.summary
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationSummary_WhenPresent_NoViolation()
    {
        var doc = BuildDocWithOperation(op => op.Summary = "Get user");
        var rule = new OperationSummaryRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationSummary_WhenMissing_OneViolation()
    {
        var doc = BuildDocWithOperation(op => op.Summary = null);
        var rule = new OperationSummaryRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.summary");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // operation.operation-id
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationOperationId_WhenPresent_NoViolation()
    {
        var doc = BuildDocWithOperation(op => op.OperationId = "GetUser");
        var rule = new OperationOperationIdRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationOperationId_WhenMissing_OneViolation()
    {
        var doc = BuildDocWithOperation(op => op.OperationId = null);
        var rule = new OperationOperationIdRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.operation-id");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // operation.description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationDescription_WhenSufficientLength_NoViolation()
    {
        var doc = BuildDocWithOperation(op => op.Description = "Returns a paginated list of users filtered by status.");
        var rule = new OperationDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationDescription_WhenTooShort_OneViolation()
    {
        // Use a description shorter than the default MinDescriptionLength (5): "OK" has 2 chars.
        var doc = BuildDocWithOperation(op => op.Description = "OK");
        var rule = new OperationDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.description");
        violations[0].Message.Should().Contain("2"); // actual length
    }

    [Fact]
    public void OperationDescription_WhenNull_OneViolation()
    {
        var doc = BuildDocWithOperation(op => op.Description = null);
        var rule = new OperationDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.description");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // operation.tags
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationTags_WhenTagsPresent_NoViolation()
    {
        var doc = BuildDocWithOperation(op =>
            op.Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Users", null) });
        var rule = new OperationTagsRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationTags_WhenNoTags_OneViolation()
    {
        // Microsoft.OpenApi v3.5.0 does not allow setting Tags to null — use Clear() instead.
        var doc = BuildDocWithOperation(op => op.Tags!.Clear());
        var rule = new OperationTagsRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.tags");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // operation.has-error-response
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationHasErrorResponse_When422Present_NoViolation()
    {
        var doc = BuildDocWithOperation(op =>
        {
            op.Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Description = "OK" },
                ["422"] = new OpenApiResponse { Description = "Error" },
            };
        });
        var rule = new OperationHasErrorResponseRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationHasErrorResponse_WhenOnlySuccess_OneViolation()
    {
        var doc = BuildDocWithOperation(op =>
        {
            op.Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Description = "OK" },
            };
        });
        var rule = new OperationHasErrorResponseRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.has-error-response");
    }

    [Fact]
    public void OperationHasErrorResponse_OnExcludedPath_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/healthz"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "OK" },
                            }
                        }
                    }
                }
            }
        };
        var rule = new OperationHasErrorResponseRule();
        // ExcludedPathPrefixes default is now empty — provide /healthz explicitly.
        var ctx = new ValidationContext { ExcludedPathPrefixes = ["/healthz"] };
        rule.Validate(doc, ctx).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // operation.deprecated-has-note
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OperationDeprecatedHasNote_WhenDeprecatedWithNote_NoViolation()
    {
        var doc = BuildDocWithOperation(op =>
        {
            op.Deprecated = true;
            op.Description = "Use /v2/users instead. This endpoint is removed in v3.";
        });
        var rule = new OperationDeprecatedHasNoteRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void OperationDeprecatedHasNote_WhenDeprecatedWithoutNote_OneViolation()
    {
        var doc = BuildDocWithOperation(op =>
        {
            op.Deprecated = true;
            op.Description = "Returns user data.";
        });
        var rule = new OperationDeprecatedHasNoteRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("operation.deprecated-has-note");
    }

    [Fact]
    public void OperationDeprecatedHasNote_WhenNotDeprecated_NoViolation()
    {
        var doc = BuildDocWithOperation(op =>
        {
            op.Deprecated = false;
            op.Description = "Returns user data.";
        });
        var rule = new OperationDeprecatedHasNoteRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildDocWithOperation(Action<OpenApiOperation> configure)
    {
        var operation = new OpenApiOperation
        {
            Summary     = "Test summary",
            OperationId = "TestOp",
            Description = "Test description that is long enough for validation.",
            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Test", null) },
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Description = "OK" },
                ["422"] = new OpenApiResponse { Description = "Error" },
            },
        };

        configure(operation);

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
