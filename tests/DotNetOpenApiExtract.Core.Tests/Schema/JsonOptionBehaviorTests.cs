using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Tests for T9 JSON serializer option behaviors in <see cref="SchemaGenerator"/>:
/// DefaultIgnoreCondition and NumberHandling.
/// </summary>
public sealed class JsonOptionBehaviorTests : IDisposable
{
    private readonly AssemblyLoader _loader;

    public JsonOptionBehaviorTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
    }

    public void Dispose() => _loader.Dispose();

    private Type GetType(string name) =>
        _loader.Assembly.GetType($"SampleApi.Models.{name}")!;

    private OpenApiSchema ResolveSchema(SchemaGenerator gen, IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference reference)
            return gen.Schemas[reference.Reference.Id!];
        return (OpenApiSchema)schema;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 20. DefaultIgnoreCondition = WhenWritingNull → nullable NOT required
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultIgnoreCondition_WhenWritingNull_NullableNotRequired()
    {
        // UserProfile has int? Age — which would normally be in required when using Required attr,
        // but with WhenWritingNull, nullable properties are not required.
        // UserProfile.Profile in UserDto is nullable reference type.
        var gen = new SchemaGenerator(new SchemaOptions
        {
            NamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        var type = GetType("UserProfile");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserProfile"];

        // firstName, lastName, age, avatarUrl are all nullable in UserProfile
        // With WhenWritingNull, none of them should be required
        if (schema.Required != null)
        {
            schema.Required.Should().NotContain("age",
                because: "int? is nullable and WhenWritingNull means it can be omitted");
            schema.Required.Should().NotContain("firstName",
                because: "string? is nullable reference and WhenWritingNull means it can be omitted");
        }
        else
        {
            // null required = empty required array — all good
            schema.Required.Should().BeNull();
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 21. DefaultIgnoreCondition = Never → baseline: nullable reference still not required
    //     (matches existing NRT behavior, not affected by Never condition)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultIgnoreCondition_Never_NullableReferenceNotRequired()
    {
        // Baseline behavior: nullable reference types (string?) are not required
        // regardless of DefaultIgnoreCondition = Never
        var gen = new SchemaGenerator(new SchemaOptions
        {
            NamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        });
        var type = GetType("UserProfile");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserProfile"];

        // firstName is string? (nullable reference) — should NOT be required
        // The NRT analysis already handles this independently of DefaultIgnoreCondition
        if (schema.Required != null)
        {
            schema.Required.Should().NotContain("firstName",
                because: "string? is nullable and not required by NRT analysis");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 23. NumberHandling = WriteAsString → STJ writes a string and reads only a number:
    //     anyOf [number, numeric string]
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NumberHandling_WriteAsString_SchemaIsNumberOrNumericString()
    {
        var generator = new SchemaGenerator(new SchemaOptions { NumberHandling = JsonNumberHandling.WriteAsString });

        var schema = (OpenApiSchema)generator.GenerateSchema(typeof(int));

        schema.AnyOf.Should().HaveCount(2);
        ((OpenApiSchema)schema.AnyOf![0]).Type.Should().Be(JsonSchemaType.Integer);
        var numericString = (OpenApiSchema)schema.AnyOf[1];
        numericString.Type.Should().Be(JsonSchemaType.String);
        numericString.Pattern.Should().Be("^[+-]?[0-9]+$");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // I1. NumberHandling combined flags — WriteAsString | AllowReadingFromString:
    //     the same union of what is written (a string) and what is read (number or string)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NumberHandling_WriteAsStringAndAllowReadingFromString_SchemaIsNumberOrNumericString()
    {
        var generator = new SchemaGenerator(new SchemaOptions
        {
            NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString,
        });

        var schema = (OpenApiSchema)generator.GenerateSchema(typeof(int));

        schema.AnyOf.Should().HaveCount(2);
        ((OpenApiSchema)schema.AnyOf![0]).Type.Should().Be(JsonSchemaType.Integer);
        ((OpenApiSchema)schema.AnyOf[1]).Pattern.Should().Be("^[+-]?[0-9]+$");
    }
}
