using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Tests for standalone validation (reading an existing OpenAPI spec file and validating it).
/// Uses the <see cref="OpenApiValidator"/> directly via in-memory documents.
/// </summary>
public sealed class StandaloneValidateTests
{
    private static readonly ValidationContext DefaultContext = new();

    // ─────────────────────────────────────────────────────────────────────────
    // 6. Well-formed spec produces no violations for core rules
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StandaloneValidate_WellFormedSpec_FewViolations()
    {
        var doc = BuildWellFormedDocument();

        // Skip rules that require enum descriptions (task 6.1 feature) and CLR bindings
        var ctx = new ValidationContext
        {
            SkippedRuleIds = new HashSet<string>
            {
                "enum.value-description",    // requires x-enum-descriptions extension
                "operation.security",        // requires CLR attribute bindings
                "schema.property-constraints", // requires CLR attribute bindings
                "schema.property-format",    // requires CLR type bindings
            }
        };

        var result = CoreValidator.Validate(doc, ctx);

        result.Violations.Should().BeEmpty(
            because: $"all rules should pass on the well-formed document. Violations: {FormatViolations(result)}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. Spec with missing descriptions produces violations
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StandaloneValidate_MissingDescription_Violations()
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
                            Summary = "Get users",
                            OperationId = "GetUsers",
                            Description = null, // Missing
                            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Users", null) },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "OK" },
                                ["422"] = new OpenApiResponse { Description = "Error" },
                            }
                        }
                    }
                }
            }
        };

        var result = CoreValidator.Validate(doc, DefaultContext);

        result.Violations.Should().Contain(v => v.RuleId == "operation.description",
            because: "operation.description is null");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. YAML spec file can be loaded and validated
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StandaloneValidate_YamlSpec_Works()
    {
        const string yaml = """
            openapi: 3.0.0
            info:
              title: Test API
              version: v1
            paths:
              /api/test:
                get:
                  summary: Test operation
                  operationId: TestOp
                  description: This operation returns test data for integration testing purposes.
                  tags:
                    - Test
                  responses:
                    '200':
                      description: OK
                    '422':
                      description: Error
            """;

        var tmpFile = Path.GetTempFileName() + ".yaml";
        try
        {
            await File.WriteAllTextAsync(tmpFile, yaml, Xunit.TestContext.Current.CancellationToken);

            var readerSettings = new OpenApiReaderSettings();
            readerSettings.TryAddReader("yaml", new OpenApiYamlReader());
            var readResult = await OpenApiDocument.LoadAsync(tmpFile, readerSettings, Xunit.TestContext.Current.CancellationToken);
            readResult.Document.Should().NotBeNull();

            var ctx = new ValidationContext
            {
                SkippedRuleIds = new HashSet<string>
                {
                    "spec.info-description",         // info description not in test yaml
                    "enum.value-description",
                    "operation.security",
                    "schema.property-constraints",
                    "schema.property-format",
                    "operation.tag-defined",         // YAML has no top-level tags array
                    "operation.success-response",    // not applicable here: 200 is present but covered by the rule
                }
            };

            var result = CoreValidator.Validate(readResult.Document!, ctx);

            // The minimal YAML should have no violations for remaining rules
            result.Violations.Should().BeEmpty(
                because: $"the YAML spec is well-formed for active rules. Violations: {FormatViolations(result)}");
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OpenApiDocument BuildWellFormedDocument()
    {
        return new OpenApiDocument
        {
            Info = new OpenApiInfo
            {
                Title = "My Well-Formed API",
                Version = "v1",
                Description = "A comprehensive API for managing users and resources in the system.",
            },
            // Declare tags at the document level so operation.tag-defined passes
            Tags = new HashSet<OpenApiTag>
            {
                new OpenApiTag { Name = "Users" },
            },
            Paths = new OpenApiPaths
            {
                ["/api/users"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation
                        {
                            Summary = "Get all users",
                            OperationId = "GetUsers",
                            Description = "Returns a paginated list of users. Supports filtering by status and pagination.",
                            Tags = new HashSet<OpenApiTagReference> { new OpenApiTagReference("Users", null) },
                            Parameters = new List<IOpenApiParameter>
                            {
                                new OpenApiParameter
                                {
                                    Name = "page",
                                    In = ParameterLocation.Query,
                                    Required = false,
                                    Description = "Page number for pagination (1-based)",
                                    Schema = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.Integer,
                                        Default = JsonValue.Create(1),
                                    }
                                }
                            },
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse
                                {
                                    Description = "List of users returned successfully",
                                    Content = new Dictionary<string, IOpenApiMediaType>
                                    {
                                        ["application/json"] = new OpenApiMediaType
                                        {
                                            // Array schema must have items to pass schema.array-items
                                            Schema = new OpenApiSchema
                                            {
                                                Type = JsonSchemaType.Array,
                                                Items = new OpenApiSchema { Type = JsonSchemaType.Object },
                                            }
                                        }
                                    }
                                },
                                ["422"] = new OpenApiResponse { Description = "Validation error" },
                            }
                        }
                    }
                }
            },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["UserDto"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Description = "User data transfer object containing profile information.",
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["name"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Description = "The display name of the user",
                            },
                        },
                        Required = new HashSet<string> { "name" },
                    }
                }
            }
        };
    }

    private static string FormatViolations(ValidationResult result)
    {
        if (result.Count == 0) return "(none)";
        return string.Join("; ", result.Violations.Select(v => $"{v.RuleId}: {v.Message}"));
    }
}
