using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Loading;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Tests for <see cref="JsonNamingPolicy"/> conversions in <see cref="SchemaGenerator"/>.
/// </summary>
public sealed class NamingPolicyTests : IDisposable
{
    private readonly AssemblyLoader _loader;

    public NamingPolicyTests()
    {
        _loader = new AssemblyLoader(TestPaths.SampleApiDll);
    }

    public void Dispose() => _loader.Dispose();

    private Type GetType(string name) =>
        _loader.Assembly.GetType($"SampleApi.Models.{name}")!;

    private static OpenApiSchema ResolveSchema(SchemaGenerator gen, IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference reference)
            return gen.Schemas[reference.Reference.Id!];
        return (OpenApiSchema)schema;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 13. Preserve — PascalCase unchanged
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamingPolicy_Preserve_KeepsPascalCase()
    {
        var gen = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.Preserve });
        var type = GetType("UserDto");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserDto"];
        schema.Properties.Should().ContainKey("DisplayName");
        schema.Properties.Should().NotContainKey("displayName");
        schema.Properties.Should().NotContainKey("display_name");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 14. CamelCase — lowers first char only
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamingPolicy_CamelCase_LowersFirstChar()
    {
        var gen = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.CamelCase });
        var type = GetType("UserDto");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserDto"];
        schema.Properties.Should().ContainKey("displayName");
        schema.Properties.Should().NotContainKey("DisplayName");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 15. SnakeCaseLower — PascalCase → snake_case_lower
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamingPolicy_SnakeCaseLower_ProducesSnakeCaseLower()
    {
        var gen = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        var type = GetType("UserDto");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserDto"];
        schema.Properties.Should().ContainKey("display_name");
        schema.Properties.Should().NotContainKey("DisplayName");
        schema.Properties.Should().NotContainKey("displayName");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 16. SnakeCaseUpper — PascalCase → SNAKE_CASE_UPPER
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamingPolicy_SnakeCaseUpper_ProducesSnakeCaseUpper()
    {
        var gen = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.SnakeCaseUpper });
        var type = GetType("UserDto");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserDto"];
        schema.Properties.Should().ContainKey("DISPLAY_NAME");
        schema.Properties.Should().NotContainKey("DisplayName");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 17. KebabCaseLower — PascalCase → kebab-case-lower
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamingPolicy_KebabCaseLower_ProducesKebabCaseLower()
    {
        var gen = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.KebabCaseLower });
        var type = GetType("UserDto");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserDto"];
        schema.Properties.Should().ContainKey("display-name");
        schema.Properties.Should().NotContainKey("DisplayName");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 18. KebabCaseUpper — PascalCase → KEBAB-CASE-UPPER
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NamingPolicy_KebabCaseUpper_ProducesKebabCaseUpper()
    {
        var gen = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.KebabCaseUpper });
        var type = GetType("UserDto");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["UserDto"];
        schema.Properties.Should().ContainKey("DISPLAY-NAME");
        schema.Properties.Should().NotContainKey("DisplayName");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 19. ApplyNamingPolicy helper — direct unit tests
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("PascalCase",     JsonNamingPolicy.CamelCase,      "pascalCase")]
    [InlineData("PascalCase",     JsonNamingPolicy.Preserve,       "PascalCase")]
    [InlineData("PascalCase",     JsonNamingPolicy.SnakeCaseLower, "pascal_case")]
    [InlineData("PascalCase",     JsonNamingPolicy.SnakeCaseUpper, "PASCAL_CASE")]
    [InlineData("PascalCase",     JsonNamingPolicy.KebabCaseLower, "pascal-case")]
    [InlineData("PascalCase",     JsonNamingPolicy.KebabCaseUpper, "PASCAL-CASE")]
    [InlineData("DisplayName",    JsonNamingPolicy.SnakeCaseLower, "display_name")]
    [InlineData("DisplayName",    JsonNamingPolicy.KebabCaseLower, "display-name")]
    [InlineData("MyPropertyName", JsonNamingPolicy.SnakeCaseLower, "my_property_name")]
    public void ApplyNamingPolicy_ProducesExpectedResult(
        string input,
        JsonNamingPolicy policy,
        string expected)
    {
        SchemaGenerator.ApplyNamingPolicy(input, policy).Should().Be(expected);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 19b. [JsonPropertyName] explicit name wins over active NamingPolicy
    //
    // When a property carries [JsonPropertyName("user_name")], that explicit name
    // must be used as-is regardless of which NamingPolicy is active.
    // Regression guard: if the naming policy is applied after the explicit name
    // is resolved, the key would be double-transformed (e.g. "user_name" →
    // "user_name" for snake_case, but "userNname" for camelCase if the policy
    // is applied to the *already-renamed* key).
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(JsonNamingPolicy.CamelCase)]
    [InlineData(JsonNamingPolicy.SnakeCaseLower)]
    [InlineData(JsonNamingPolicy.KebabCaseLower)]
    [InlineData(JsonNamingPolicy.Preserve)]
    public void NamingPolicy_JsonPropertyNameExplicit_WinsOverPolicy(JsonNamingPolicy policy)
    {
        // JsonCustomModel.UserName carries [JsonPropertyName("user_name")].
        // No matter which policy is active, the property key must be "user_name".
        var gen = new SchemaGenerator(new SchemaOptions { NamingPolicy = policy });
        var type = GetType("JsonCustomModel");
        gen.GenerateSchema(type);

        var schema = gen.Schemas["JsonCustomModel"];
        schema.Properties.Should().ContainKey("user_name",
            because: "[JsonPropertyName(\"user_name\")] must be used as-is regardless of NamingPolicy");
        schema.Properties.Should().NotContainKey("UserName",
            because: "original C# name must not appear — the explicit rename takes effect");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 20. Acronym handling — XMLHttpRequest with various policies
    // Fixed expected behavior: documented as-is to prevent regression.
    // CamelCase: only first char lowered → xMLHttpRequest
    // SnakeCaseLower: each transition lower→upper or UPPER→Lower inserts separator
    //                XMLHttpRequest: XML→Http transition = xml_http, Http→Request = _request
    //                → xml_http_request
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("XMLHttpRequest", JsonNamingPolicy.CamelCase,      "xMLHttpRequest")]
    [InlineData("XMLHttpRequest", JsonNamingPolicy.SnakeCaseLower, "xml_http_request")]
    [InlineData("XMLHttpRequest", JsonNamingPolicy.KebabCaseLower, "xml-http-request")]
    [InlineData("XMLHttpRequest", JsonNamingPolicy.Preserve,       "XMLHttpRequest")]
    public void ApplyNamingPolicy_Acronyms_MatchDocumentedBehavior(
        string input,
        JsonNamingPolicy policy,
        string expected)
    {
        SchemaGenerator.ApplyNamingPolicy(input, policy).Should().Be(expected);
    }
}
