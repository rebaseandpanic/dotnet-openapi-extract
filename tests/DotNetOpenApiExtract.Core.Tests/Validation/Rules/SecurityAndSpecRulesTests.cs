using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for security and spec-level validation rules.
/// </summary>
public sealed class SecurityAndSpecRulesTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // security.scheme-defined
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SecuritySchemeDefined_WhenSchemeDefined_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        Description = "JWT auth",
                    }
                }
            },
            Paths = new OpenApiPaths
            {
                ["/api/secure"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            Summary = "Secure",
                            OperationId = "SecureOp",
                            Description = "Secure operation requiring Bearer auth.",
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } },
                            Security = new List<OpenApiSecurityRequirement>
                            {
                                new OpenApiSecurityRequirement
                                {
                                    { new OpenApiSecuritySchemeReference("Bearer", null, null), new List<string>() }
                                }
                            }
                        }
                    }
                }
            }
        };
        var rule = new SecuritySchemeDefinedRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SecuritySchemeDefined_WhenSchemeNotDefined_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Paths = new OpenApiPaths
            {
                ["/api/secure"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            Summary = "Secure",
                            OperationId = "SecureOp",
                            Description = "Secure operation.",
                            Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } },
                            Security = new List<OpenApiSecurityRequirement>
                            {
                                new OpenApiSecurityRequirement
                                {
                                    { new OpenApiSecuritySchemeReference("UndefinedScheme", null, null), new List<string>() }
                                }
                            }
                        }
                    }
                }
            }
        };
        var rule = new SecuritySchemeDefinedRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("security.scheme-defined");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // security.scheme-description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SecuritySchemeDescription_WhenPresent_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        Description = "JWT Bearer authentication.",
                    }
                }
            }
        };
        var rule = new SecuritySchemeDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SecuritySchemeDescription_WhenMissing_OneViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        Description = null,
                    }
                }
            }
        };
        var rule = new SecuritySchemeDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("security.scheme-description");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // spec.info-title
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpecInfoTitle_WhenPresent_NoViolation()
    {
        var doc = new OpenApiDocument { Info = new OpenApiInfo { Title = "My API", Version = "v1" } };
        var rule = new SpecInfoTitleRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SpecInfoTitle_WhenMissing_OneViolation()
    {
        var doc = new OpenApiDocument { Info = new OpenApiInfo { Title = null, Version = "v1" } };
        var rule = new SpecInfoTitleRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("spec.info-title");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // spec.info-description
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SpecInfoDescription_WhenSufficientLength_NoViolation()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "API",
                Version = "v1",
                Description = "A comprehensive API for managing user accounts and operations.",
            }
        };
        var rule = new SpecInfoDescriptionRule();
        rule.Validate(doc, DefaultContext).Should().BeEmpty();
    }

    [Fact]
    public void SpecInfoDescription_WhenTooShort_OneViolation()
    {
        // Use a description shorter than the default MinDescriptionLength (5): "OK" has 2 chars.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "API",
                Version = "v1",
                Description = "OK",
            }
        };
        var rule = new SpecInfoDescriptionRule();
        var violations = rule.Validate(doc, DefaultContext).ToList();
        violations.Should().HaveCount(1);
        violations[0].RuleId.Should().Be("spec.info-description");
    }
}
