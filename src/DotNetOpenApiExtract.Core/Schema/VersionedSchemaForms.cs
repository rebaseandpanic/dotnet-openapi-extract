using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Schema forms that depend on the target version, chosen while the schema is generated (never
/// rewritten afterwards).
/// </summary>
internal static class VersionedSchemaForms
{
    /// <summary>
    /// A schema that accepts exactly <paramref name="value"/> (a string or an integer), typed by
    /// the value's JSON type: <c>const</c> for 3.1+, a one-element <c>enum</c> for 3.0. An integer
    /// is always a one-element <c>enum</c>, which is equivalent: Microsoft.OpenApi models
    /// <c>const</c> as a string and would write the number as <c>"1"</c>.
    /// </summary>
    public static OpenApiSchema SingleValue(object value, OpenApiSpecVersion version)
    {
        if (value is string text)
        {
            return version == OpenApiSpecVersion.OpenApi3_0
                ? new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create(text)] }
                : new OpenApiSchema { Type = JsonSchemaType.String, Const = text };
        }

        if (value is int number)
            return new OpenApiSchema { Type = JsonSchemaType.Integer, Enum = [JsonValue.Create(number)] };

        throw new ArgumentException($"Unsupported single value of type {value.GetType()}.", nameof(value));
    }

    /// <summary>
    /// Sets the example of <paramref name="schema"/> in the form of the version: <c>example: v</c> for
    /// 3.0, <c>examples: [v]</c> for 3.1+, where <c>example</c> is deprecated. A JSON <c>null</c> is
    /// <see cref="JsonNullSentinel.JsonNull"/>.
    /// </summary>
    public static void SetExample(OpenApiSchema schema, JsonNode value, OpenApiSpecVersion version)
    {
        if (version == OpenApiSpecVersion.OpenApi3_0)
            schema.Example = value;
        else
            schema.Examples = [value];
    }

    /// <summary>
    /// A base64 string (<c>byte[]</c>, <c>[Base64String] string</c>): <c>format: byte</c> for 3.0;
    /// <c>contentEncoding: base64</c> without <c>format</c> for 3.1+, where <c>byte</c> is not in the
    /// format registry. Nothing of the other form is set, so 3.0 output carries no
    /// <c>x-jsonschema-contentEncoding</c>.
    /// </summary>
    public static OpenApiSchema Base64(OpenApiSpecVersion version) =>
        version == OpenApiSpecVersion.OpenApi3_0
            ? new OpenApiSchema { Type = JsonSchemaType.String, Format = "byte" }
            : new OpenApiSchema { Type = JsonSchemaType.String, ContentEncoding = "base64" };

    /// <summary>
    /// The nullable form of an <c>anyOf</c> union: a branch that admits <c>null</c>
    /// (<see cref="NullBranch"/>). A <c>nullable</c> or <c>type: "null"</c> beside <c>anyOf</c> would
    /// not do: OpenAPI 3.0 applies <c>nullable</c> only next to an explicit <c>type</c>, and in 3.1+ a
    /// sibling <c>type: "null"</c> would require every value to be both.
    /// </summary>
    public static OpenApiSchema NullableUnion(OpenApiSchema union, OpenApiSpecVersion version)
    {
        union.AnyOf!.Add(NullBranch(version));
        return union;
    }

    /// <summary>
    /// The <c>anyOf</c> branch that admits only <c>null</c>, by version: <c>{type: "null"}</c> for 3.1+;
    /// for 3.0, which has no <c>null</c> type, <c>{type: "object", nullable: true, enum: [null]}</c>.
    /// OpenAPI 3.0.3 applies <c>nullable</c> only when <c>type</c> is defined in the same schema object
    /// (Microsoft.OpenApi writes a bare <c>null</c> type as <c>{enum: [null], nullable: true}</c>,
    /// which admits nothing under that rule); <c>enum: [null]</c> keeps the branch to <c>null</c> alone,
    /// whatever <c>type</c> says.
    /// </summary>
    public static OpenApiSchema NullBranch(OpenApiSpecVersion version) =>
        version == OpenApiSpecVersion.OpenApi3_0
            ? new OpenApiSchema { Type = JsonSchemaType.Object | JsonSchemaType.Null, Enum = [null!] }
            : new OpenApiSchema { Type = JsonSchemaType.Null };

    /// <summary>
    /// The nullable form of an enum schema, by version: <c>anyOf: [enum schema, null branch]</c>, the form of
    /// a nullable reference. The enum schema stays as it is — its single <c>type</c>, its <c>enum</c>
    /// without <c>null</c>, <c>x-enum-varnames</c> / <c>x-enum-descriptions</c> in step with <c>enum</c>.
    /// A <c>null</c> type (3.1+) or <c>nullable: true</c> (3.0) next to <c>enum</c> would not admit
    /// <c>null</c>: JSON Schema 2020-12 checks <c>type</c> and <c>enum</c> together, and OpenAPI 3.0.3 asks
    /// for <c>null</c> among the values of a nullable enum. The union carries the description of the enum
    /// schema, since an enum place is described by its enum type.
    /// </summary>
    public static OpenApiSchema NullableEnum(OpenApiSchema enumSchema, OpenApiSpecVersion version) => new()
    {
        Description = enumSchema.Description,
        AnyOf = [enumSchema, NullBranch(version)],
    };

    /// <summary>
    /// The enum schema of <paramref name="schema"/> when <paramref name="schema"/> is the
    /// <see cref="NullableEnum"/> form of either version, otherwise <see langword="null"/>. An empty
    /// <c>enum</c> counts too: the generator never writes one, but a rule reads a document as it is.
    /// </summary>
    public static OpenApiSchema? NullableEnumBranch(IOpenApiSchema? schema) =>
        schema is OpenApiSchema { Type: null, AllOf: null, AnyOf: [OpenApiSchema { Type: not null, Enum: not null } enumSchema, var nullBranch] }
        && IsNullBranch(nullBranch)
            ? enumSchema
            : null;

    /// <summary>
    /// The schema that holds the constraints of a nullable enum place — lengths, <c>pattern</c>,
    /// <c>format</c>, the bounds of <c>[Range]</c>: the enum branch of <paramref name="schema"/> when it is
    /// the nullable enum union, or of the first element of its <c>allOf</c> when allowed values wrapped
    /// the union (3.0); otherwise <paramref name="schema"/> itself.
    /// </summary>
    public static OpenApiSchema ConstraintTarget(OpenApiSchema schema) =>
        NullableEnumBranch(schema)
        ?? (schema.AllOf is [var first, ..] ? NullableEnumBranch(first) : null)
        ?? schema;

    /// <summary>
    /// Whether <paramref name="schema"/> is a <see cref="NullBranch"/> of either version, built or read
    /// from a file (the reader holds a <c>null</c> of <c>enum</c> as <see cref="JsonNullSentinel.JsonNull"/>).
    /// </summary>
    public static bool IsNullBranch(IOpenApiSchema? schema) => schema switch
    {
        OpenApiSchema { Type: JsonSchemaType.Null } => true,
        OpenApiSchema { Type: { } type, Enum: [var only] } => (type & JsonSchemaType.Null) != 0
            && (only is null || JsonNullSentinel.IsJsonNullSentinel(only)),
        _ => false,
    };
}
