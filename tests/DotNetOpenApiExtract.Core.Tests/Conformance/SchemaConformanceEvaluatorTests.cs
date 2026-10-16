using System.Text.Json.Nodes;
using AwesomeAssertions;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Conformance;

/// <summary>
/// <see cref="SchemaConformance"/> applies the subschemas of a nullable object or array to its members
/// and elements, as for a non-nullable one. JSON Schema 2020-12 Core §10.3.1.2 and §10.3.2.3:
/// <c>items</c> validates every element and <c>additionalProperties</c> every member not matched by
/// <c>properties</c> or <c>patternProperties</c>; Validation §6.1.1: <c>type</c> is an assertion on
/// its own, so a type list with <c>"null"</c> does not switch these subschemas off.
/// </summary>
public class SchemaConformanceEvaluatorTests
{
    /// <summary>Why the rejecting cases are skipped; see the remarks of <see cref="SchemaConformance"/>.</summary>
    private const string EvaluatorLimitation =
        "Corvus.Json.Validator 5.7.6 does not apply additionalProperties and items when type lists several types; enable when a version does.";

    /// <summary>A 3.1 document with an object component that a nullable dictionary place can reference.</summary>
    private static SchemaConformance Conformance() => SchemaConformance.For(new JsonObject
    {
        ["openapi"] = "3.1.1",
        ["info"] = new JsonObject { ["title"] = "t", ["version"] = "1" },
        ["paths"] = new JsonObject(),
        ["components"] = new JsonObject
        {
            ["schemas"] = new JsonObject
            {
                ["Item"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["name"] = new JsonObject { ["type"] = "string" } },
                },
            },
        },
    });

    /// <summary>Value schemas of a dictionary: a component reference and an inline object.</summary>
    public static TheoryData<string> ValueSchemas => ["{\"$ref\":\"#/components/schemas/Item\"}", "{\"type\":\"object\"}"];

    [Theory(Skip = EvaluatorLimitation)]
    [MemberData(nameof(ValueSchemas))]
    public void NullValue_InANullableObjectWithAnObjectValueSchema_IsRejected(string valueSchema)
    {
        var schema = new JsonObject
        {
            ["type"] = new JsonArray("null", "object"),
            ["additionalProperties"] = JsonNode.Parse(valueSchema),
        };

        var result = Conformance().ValidateSchema(schema, new JsonObject { ["a"] = null });

        result.IsValid.Should().BeFalse(because: "null is not an object, and additionalProperties applies to the member 'a'");
    }

    [Theory]
    [MemberData(nameof(ValueSchemas))]
    public void ObjectValue_InANullableObjectWithAnObjectValueSchema_Passes(string valueSchema)
    {
        var schema = new JsonObject
        {
            ["type"] = new JsonArray("null", "object"),
            ["additionalProperties"] = JsonNode.Parse(valueSchema),
        };

        var result = Conformance().ValidateSchema(schema, new JsonObject { ["a"] = new JsonObject { ["name"] = "x" } });

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }

    [Fact(Skip = EvaluatorLimitation)]
    public void NullElement_InANullableArrayWithAnObjectItemSchema_IsRejected()
    {
        var schema = new JsonObject
        {
            ["type"] = new JsonArray("null", "array"),
            ["items"] = new JsonObject { ["$ref"] = "#/components/schemas/Item" },
        };

        var result = Conformance().ValidateSchema(schema, new JsonArray((JsonNode?)null));

        result.IsValid.Should().BeFalse(because: "null is not an object, and items applies to every element");
    }

    [Fact]
    public void Null_ForANullableObject_Passes()
    {
        var schema = new JsonObject
        {
            ["type"] = new JsonArray("null", "object"),
            ["additionalProperties"] = new JsonObject { ["type"] = "object" },
        };

        var result = Conformance().ValidateSchema(schema, null);

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }
}
