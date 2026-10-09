using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;

namespace DotNetOpenApiExtract.Core.Tests.Validation;

/// <summary>
/// Validation rules see operations of any method and in any root: <c>webhooks</c>, <c>query</c>,
/// <c>additionalOperations</c>; pointers follow the output of the validated version.
/// </summary>
public class AnyMethodValidationRulesTests
{
    private static readonly IReadOnlySet<string> AllRules = CoreValidator.AllRuleIds.ToHashSet();

    // ── Webhooks ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Operation rules: every rule that walks operations, except <c>path.params-match</c> and
    /// <c>parameter.path-required-true</c> (path-template rules: a webhook key is a name, not a
    /// template) and <c>operation.security</c> (it needs the build-time action bindings that a
    /// foreign document does not have).
    /// </summary>
    public static TheoryData<string> OperationRules =>
    [
        "operation.summary", "operation.description", "operation.operation-id",
        "operation.operation-id-unique", "operation.operation-id-url-safe", "operation.operation-id-pascal-case",
        "operation.tags", "operation.tag-defined", "operation.deprecated-has-note",
        "operation.success-response", "operation.has-error-response", "operation.has-required-response-codes",
        "operation.parameters-unique", "operation.request-body-description",
        "parameter.description", "parameter.optional-has-default", "parameter.schema-type",
        "response.description", "response.content-type-json-default", "response.schema-when-body",
        "schema.array-items", "spec.no-eval-in-markdown", "spec.no-script-tags-in-markdown",
        "security.scheme-defined", "spec.no-ref-siblings",
    ];

    [Theory]
    [MemberData(nameof(OperationRules))]
    public void OperationRule_ReportsViolationInsideWebhook(string ruleId)
    {
        AllRules.Should().Contain(ruleId);

        // spec.no-ref-siblings applies to 3.0 documents only.
        var version = ruleId == "spec.no-ref-siblings" ? OpenApiSpecVersion.OpenApi3_0 : OpenApiSpecVersion.OpenApi3_1;
        var result = CoreValidator.Validate(WebhooksOnlyDocument(), Context(version));

        result.SkippedRules.Should().NotContain(ruleId);
        result.Violations.Where(v => v.RuleId == ruleId)
            .Should().Contain(v => v.JsonPointer.StartsWith("#/webhooks/", StringComparison.Ordinal));
    }

    [Fact]
    public void ComponentReferencedOnlyFromWebhook_IsUsed()
    {
        var document = WebhooksOnlyDocument();
        var result = CoreValidator.Validate(document, Context(OpenApiSpecVersion.OpenApi3_1));

        result.SkippedRules.Should().NotContain("component.no-unused");
        result.Violations.Where(v => v.RuleId == "component.no-unused")
            .Should().NotContain(v => v.JsonPointer == "#/components/schemas/Payload");
        result.Violations.Where(v => v.RuleId == "component.no-unused")
            .Should().Contain(v => v.JsonPointer == "#/components/schemas/Orphan",
                because: "the rule does run: an unreferenced component is still reported");
    }

    // ── operationId uniqueness ───────────────────────────────────────────────

    [Fact]
    public void OperationIdUnique_SeesQueryAndWebhooks_IgnoresMissingIds()
    {
        var document = new OpenApiDocument
        {
            Info  = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths
            {
                ["/a"] = Item(
                    (HttpMethod.Get, Op("SameInPath")),
                    (HttpMethod.Parse("QUERY"), Op("SameInPath")),
                    (HttpMethod.Post, Op(null)),
                    (HttpMethod.Put, Op(null))),
                ["/b"] = Item((HttpMethod.Get, Op("AcrossRoots"))),
            },
            Webhooks = new Dictionary<string, IOpenApiPathItem>
            {
                ["event"] = Item((HttpMethod.Post, Op("AcrossRoots"))),
            },
        };

        var result = CoreValidator.Validate(document, Context(OpenApiSpecVersion.OpenApi3_2));
        var unique = result.Violations.Where(v => v.RuleId == "operation.operation-id-unique").ToList();

        result.SkippedRules.Should().NotContain("operation.operation-id-unique");
        unique.Should().Contain(v => v.JsonPointer == "#/paths/~1a/query");
        unique.Should().Contain(v => v.JsonPointer == "#/webhooks/event/post");
        unique.Should().NotContain(v => v.JsonPointer == "#/paths/~1a/post" || v.JsonPointer == "#/paths/~1a/put",
            because: "operations without an operationId do not conflict");
    }

    // ── --require-response-code groups ───────────────────────────────────────

    [Fact]
    public void RequiredResponseCodes_QueryAndTraceAreSafe_LinkIsMutating()
    {
        var document = new OpenApiDocument
        {
            Info  = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths
            {
                ["/s"] = Item(
                    (HttpMethod.Parse("QUERY"), Op("Q")),
                    (HttpMethod.Trace, Op("T")),
                    (HttpMethod.Parse("LINK"), Op("L"))),
            },
        };
        var context = new ValidationContext
        {
            OpenApiSpecVersion    = OpenApiSpecVersion.OpenApi3_1,
            EnabledRuleIds        = new HashSet<string>(CoreValidator.DefaultOffRuleIds),
            RequiredResponseCodes = [("safe", 400), ("mutating", 422)],
        };

        var pointers = CoreValidator.Validate(document, context).Violations
            .Where(v => v.RuleId == "operation.has-required-response-codes")
            .Select(v => v.JsonPointer)
            .ToList();

        pointers.Should().BeEquivalentTo(
        [
            "#/paths/~1s/x-oai-additionalOperations/QUERY/responses/400",
            "#/paths/~1s/trace/responses/400",
            "#/paths/~1s/x-oai-additionalOperations/LINK/responses/422",
        ]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ValidationContext Context(OpenApiSpecVersion version) => new()
    {
        OpenApiSpecVersion    = version,
        EnabledRuleIds        = new HashSet<string>(CoreValidator.DefaultOffRuleIds),
        RequiredResponseCodes = [("*", 418)],
    };

    private static OpenApiOperation Op(string? operationId) => new()
    {
        OperationId = operationId,
        Summary     = "s",
        Responses   = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "ok" } },
    };

    private static OpenApiPathItem Item(params (HttpMethod Method, OpenApiOperation Operation)[] operations)
    {
        var item = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
        foreach (var (method, operation) in operations)
            item.Operations[method] = operation;
        return item;
    }

    /// <summary>
    /// A document whose only operations are webhooks, each crafted to break operation rules.
    /// </summary>
    private static OpenApiDocument WebhooksOnlyDocument()
    {
        var document = new OpenApiDocument
        {
            Info       = new OpenApiInfo { Title = "t", Version = "1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Payload"] = new OpenApiSchema { Type = JsonSchemaType.Object, Description = "payload" },
                    ["Orphan"]  = new OpenApiSchema { Type = JsonSchemaType.Object, Description = "orphan" },
                },
            },
        };

        var duplicated = new OpenApiParameter
        {
            Name = "page", In = ParameterLocation.Query, Required = false,
            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
        };

        // Breaks the operation, parameter, request body, response and security rules.
        var broken = new OpenApiOperation
        {
            Deprecated  = true,
            Parameters  =
            [
                duplicated,
                duplicated,
                new OpenApiParameter { Name = "raw", In = ParameterLocation.Query, Description = "raw" },
            ],
            RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchemaReference("Payload", document) { Description = "sibling of $ref" },
                    },
                    ["application/x-list"] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.Array } },
                },
            },
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "",
                    Content = new Dictionary<string, IOpenApiMediaType> { ["text/plain"] = new OpenApiMediaType() },
                },
            },
            Security =
            [
                new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Ghost", document)] = [] },
            ],
        };

        var noSuccess = new OpenApiOperation
        {
            OperationId = "NoSuccess",
            Summary     = "s",
            Description = "d",
            Tags        = new HashSet<OpenApiTagReference> { new("Undefined", document) },
            Responses   = new OpenApiResponses { ["500"] = new OpenApiResponse { Description = "error" } },
        };

        var markdown = new OpenApiOperation
        {
            OperationId = "Markdown",
            Summary     = "s",
            Description = "<script>eval(1)</script>",
            Responses   = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "ok" } },
        };

        document.Webhooks = new Dictionary<string, IOpenApiPathItem>
        {
            ["broken"]     = Item((HttpMethod.Post, broken)),
            ["no-success"] = Item((HttpMethod.Post, noSuccess)),
            ["markdown"]   = Item((HttpMethod.Post, markdown)),
            ["ids-1"]      = Item((HttpMethod.Post, Op("bad id"))),
            ["ids-2"]      = Item((HttpMethod.Post, Op("bad id"))),
        };
        return document;
    }
}
