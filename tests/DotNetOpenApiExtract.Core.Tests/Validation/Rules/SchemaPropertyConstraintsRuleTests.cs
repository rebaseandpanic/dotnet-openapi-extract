using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Validation.Rules;
using Microsoft.OpenApi;
using Xunit;

// Disambiguate: System.ComponentModel.DataAnnotations also has a ValidationContext type.
using ValidationContext = DotNetOpenApiExtract.Core.Validation.ValidationContext;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// Unit tests for <see cref="SchemaPropertyConstraintsRule"/>.
///
/// Each test constructs a synthetic <see cref="OpenApiDocument"/> with one schema, wires
/// <see cref="ValidationContext.TypeBySchemaId"/> to a fixture CLR type defined in this file,
/// and invokes the rule directly.  This directly exercises the array vs. string dispatch
/// logic without hitting the full extraction pipeline.
/// </summary>
public sealed class SchemaPropertyConstraintsRuleTests
{
    private static readonly SchemaPropertyConstraintsRule Rule = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Fixture CLR types — properties carry the real attributes so the rule
    // can read GetCustomAttributesData() exactly as it does in production.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class ArrayMaxLengthFixture
    {
        [MaxLength(10)]
        public List<string> Items { get; set; } = [];
    }

    private sealed class ArrayMinLengthFixture
    {
        [MinLength(2)]
        public List<string> RequiredItems { get; set; } = [];
    }

    private sealed class StringMaxLengthFixture
    {
        [MaxLength(100)]
        public string? Name { get; set; }
    }

    private sealed class StringLengthFixture
    {
        [StringLength(255)]
        public string? Email { get; set; }
    }

    private sealed class StringMinLengthFixture
    {
        [MinLength(5)]
        public string? LongText { get; set; }
    }

    private sealed class StringLengthOnArrayFixture
    {
        [StringLength(10)]
        public List<string> Tags { get; set; } = [];
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static (OpenApiDocument Doc, ValidationContext Context) BuildArrayDoc(
        string schemaId,
        string propName,
        Type fixtureType,
        Action<OpenApiSchema> configureProp)
    {
        var prop = new OpenApiSchema { Type = JsonSchemaType.Array };
        configureProp(prop);

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    [schemaId] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            [propName] = prop,
                        },
                    }
                }
            }
        };

        var ctx = new ValidationContext
        {
            TypeBySchemaId = new Dictionary<string, Type> { [schemaId] = fixtureType }
        };

        return (doc, ctx);
    }

    private static (OpenApiDocument Doc, ValidationContext Context) BuildStringDoc(
        string schemaId,
        string propName,
        Type fixtureType,
        Action<OpenApiSchema> configureProp)
    {
        var prop = new OpenApiSchema { Type = JsonSchemaType.String };
        configureProp(prop);

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    [schemaId] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            [propName] = prop,
                        },
                    }
                }
            }
        };

        var ctx = new ValidationContext
        {
            TypeBySchemaId = new Dictionary<string, Type> { [schemaId] = fixtureType }
        };

        return (doc, ctx);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Array + [MaxLength] + maxItems set → no violation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxLength_ArrayWithMaxItemsSet_NoViolation()
    {
        var (doc, ctx) = BuildArrayDoc(
            "ArrayMaxLengthFixture", "Items", typeof(ArrayMaxLengthFixture),
            prop => prop.MaxItems = 10);

        Rule.Validate(doc, ctx).Should().BeEmpty(
            because: "array schema has maxItems set, so [MaxLength] is satisfied");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Array + [MaxLength] + maxItems absent → violation with "maxItems"
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxLength_ArrayWithMaxItemsAbsent_OneViolationMentioningMaxItems()
    {
        var (doc, ctx) = BuildArrayDoc(
            "ArrayMaxLengthFixture", "Items", typeof(ArrayMaxLengthFixture),
            prop => { /* MaxItems not set */ });

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "array schema lacks maxItems for a [MaxLength]-annotated property");
        violations[0].RuleId.Should().Be("schema.property-constraints");
        violations[0].Message.Should().Contain("maxItems",
            because: "array constraint keyword is maxItems, not maxLength");
        violations[0].Message.Should().NotContain("maxLength",
            because: "maxLength is the wrong keyword for array schemas");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Array + [MinLength] + minItems set → no violation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MinLength_ArrayWithMinItemsSet_NoViolation()
    {
        var (doc, ctx) = BuildArrayDoc(
            "ArrayMinLengthFixture", "RequiredItems", typeof(ArrayMinLengthFixture),
            prop => prop.MinItems = 2);

        Rule.Validate(doc, ctx).Should().BeEmpty(
            because: "array schema has minItems set, so [MinLength] is satisfied");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Array + [MinLength] + minItems absent → violation with "minItems"
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MinLength_ArrayWithMinItemsAbsent_OneViolationMentioningMinItems()
    {
        var (doc, ctx) = BuildArrayDoc(
            "ArrayMinLengthFixture", "RequiredItems", typeof(ArrayMinLengthFixture),
            prop => { /* MinItems not set */ });

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "array schema lacks minItems for a [MinLength]-annotated property");
        violations[0].RuleId.Should().Be("schema.property-constraints");
        violations[0].Message.Should().Contain("minItems",
            because: "array constraint keyword is minItems, not minLength");
        violations[0].Message.Should().NotContain("minLength",
            because: "minLength is the wrong keyword for array schemas");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. String + [MaxLength] + maxLength set → no violation (regression guard)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxLength_StringWithMaxLengthSet_NoViolation()
    {
        var (doc, ctx) = BuildStringDoc(
            "StringMaxLengthFixture", "Name", typeof(StringMaxLengthFixture),
            prop => prop.MaxLength = 100);

        Rule.Validate(doc, ctx).Should().BeEmpty(
            because: "string schema has maxLength set, so [MaxLength] is satisfied");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. String + [MaxLength] + maxLength absent → violation with "maxLength"
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxLength_StringWithMaxLengthAbsent_OneViolationMentioningMaxLength()
    {
        var (doc, ctx) = BuildStringDoc(
            "StringMaxLengthFixture", "Name", typeof(StringMaxLengthFixture),
            prop => { /* MaxLength not set */ });

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "string schema lacks maxLength for a [MaxLength]-annotated property");
        violations[0].RuleId.Should().Be("schema.property-constraints");
        violations[0].Message.Should().Contain("maxLength",
            because: "string constraint keyword is maxLength");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. String + [StringLength] + maxLength absent → violation (StringLength
    //    always maps to maxLength, regardless of schema type)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StringLength_StringWithMaxLengthAbsent_OneViolationMentioningMaxLength()
    {
        var (doc, ctx) = BuildStringDoc(
            "StringLengthFixture", "Email", typeof(StringLengthFixture),
            prop => { /* MaxLength not set */ });

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "[StringLength] is string-only by contract and always maps to maxLength");
        violations[0].RuleId.Should().Be("schema.property-constraints");
        violations[0].Message.Should().Contain("maxLength",
            because: "StringLength always maps to the maxLength OpenAPI keyword");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 8. Standalone mode (TypeBySchemaId == null) → no violations produced
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StandaloneMode_TypeBySchemaIdNull_NoViolations()
    {
        // In standalone mode (no assembly), the rule must be silently skipped.
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["ArrayMaxLengthFixture"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["Items"] = new OpenApiSchema { Type = JsonSchemaType.Array },
                        },
                    }
                }
            }
        };

        var ctx = new ValidationContext(); // TypeBySchemaId is null by default

        Rule.Validate(doc, ctx).Should().BeEmpty(
            because: "standalone mode has no CLR bindings and the rule must be skipped entirely");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 9. String + [MinLength] + minLength set → no violation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MinLength_StringWithMinLengthSet_NoViolation()
    {
        var (doc, ctx) = BuildStringDoc(
            "StringMinLengthFixture", "LongText", typeof(StringMinLengthFixture),
            prop => prop.MinLength = 5);

        Rule.Validate(doc, ctx).Should().BeEmpty(
            because: "string schema has minLength set, so [MinLength] is satisfied");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 10. String + [MinLength] + minLength absent → violation with "minLength"
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MinLength_StringWithMinLengthAbsent_OneViolationMentioningMinLength()
    {
        var (doc, ctx) = BuildStringDoc(
            "StringMinLengthFixture", "LongText", typeof(StringMinLengthFixture),
            prop => { /* MinLength not set */ });

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "string schema lacks minLength for a [MinLength]-annotated property");
        violations[0].RuleId.Should().Be("schema.property-constraints");
        violations[0].Message.Should().Contain("minLength",
            because: "string constraint keyword is minLength, not minItems");
        violations[0].Message.Should().NotContain("minItems",
            because: "minItems is the wrong keyword for string schemas");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 11. Array + [StringLength] → violation mentions "maxLength" not "maxItems"
    //     [StringLength] is string-only by design; the rule unconditionally maps
    //     it to maxLength regardless of the schema type.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StringLength_ArrayProperty_ViolationMentionsMaxLengthNotMaxItems()
    {
        var (doc, ctx) = BuildArrayDoc(
            "StringLengthOnArrayFixture", "Tags", typeof(StringLengthOnArrayFixture),
            prop => { /* MaxLength and MaxItems not set */ });

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "[StringLength] always routes to the maxLength path regardless of schema type");
        violations[0].RuleId.Should().Be("schema.property-constraints");
        violations[0].Message.Should().Contain("maxLength",
            because: "[StringLength] unconditionally maps to the maxLength OpenAPI keyword");
        violations[0].Message.Should().NotContain("maxItems",
            because: "maxItems is for [MaxLength] on array schemas, not [StringLength]");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 12. Nullable array schema (Type = Array | Null) + [MaxLength] + maxItems
    //     absent → violation mentioning "maxItems".
    //
    //     Pins HasFlag-based detection against a future refactor that might
    //     use `==` (which would miss the Array | Null combined value = 65).
    //     SchemaGenerator emits nullable arrays as Type = Array | Null.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxLength_NullableArrayWithMaxItemsAbsent_ViolationMentionsMaxItems()
    {
        var prop = new OpenApiSchema
        {
            // Combined bit flags — 1 (Array) | 64 (Null) = 65.
            Type = JsonSchemaType.Array | JsonSchemaType.Null,
        };

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["ArrayMaxLengthFixture"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["Items"] = prop,
                        },
                    }
                }
            }
        };

        var ctx = new ValidationContext
        {
            TypeBySchemaId = new Dictionary<string, Type>
            {
                ["ArrayMaxLengthFixture"] = typeof(ArrayMaxLengthFixture)
            }
        };

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "nullable array schema still carries the Array flag and must be treated as an array");
        violations[0].Message.Should().Contain("maxItems",
            because: "HasFlag(Array) must be true for Array|Null — a future refactor to '==' would silently break this");
        violations[0].Message.Should().NotContain("maxLength",
            because: "the nullable array must not be treated as a string schema");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 13. Property in schema but not on CLR type → rule silently skips.
    //     Documented design: line 47 `if (clrProp == null) continue;`.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class EmptyFixture
    {
        // No properties — any schema property name cannot be matched.
        public int Irrelevant { get; set; }
    }

    [Fact]
    public void PropertyNotFoundOnClrType_NoViolation()
    {
        var (doc, ctx) = BuildArrayDoc(
            "EmptyFixture", "PropThatDoesNotExistOnClrType", typeof(EmptyFixture),
            prop => { /* MaxItems not set — would normally trigger for a real CLR property */ });

        Rule.Validate(doc, ctx).Should().BeEmpty(
            because: "when the schema property has no matching CLR property the rule must skip silently (no CLR attrs available to check)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 14. Property with null Type (e.g. inline schema without an explicit type,
    //     or a schema whose type was not emitted) on a [MaxLength] CLR property
    //     → rule falls through to the non-array branch and reports missing
    //     'maxLength'.  Pinned so a future tweak to the HasValue guard is
    //     caught by tests.
    //
    //     Note: $ref-wrapped properties are filtered OUT earlier by the rule
    //     (IOpenApiSchema is not OpenApiSchema for references), so this test
    //     covers only inline schemas without an explicit type.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MaxLength_InlineSchemaWithNullType_FallsThroughToMaxLengthBranch()
    {
        var prop = new OpenApiSchema { /* Type not set — null */ };

        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "T", Version = "v1" },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["ArrayMaxLengthFixture"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["Items"] = prop,
                        },
                    }
                }
            }
        };

        var ctx = new ValidationContext
        {
            TypeBySchemaId = new Dictionary<string, Type>
            {
                ["ArrayMaxLengthFixture"] = typeof(ArrayMaxLengthFixture)
            }
        };

        var violations = Rule.Validate(doc, ctx).ToList();

        violations.Should().HaveCount(1,
            because: "a schema with null Type does not satisfy HasValue, so the array branch is skipped and the rule falls through to the maxLength check");
        violations[0].Message.Should().Contain("maxLength",
            because: "the non-array branch reports the 'maxLength' keyword");
        violations[0].Message.Should().NotContain("maxItems",
            because: "the array branch must not fire when Type is null");
    }
}
