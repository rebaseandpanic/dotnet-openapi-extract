using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Tests.Conformance;
using DotNetOpenApiExtract.Core.Tests.Harness;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using ModernApi.Models.Contexts;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Integration tests verifying that JSON options detected via Roslyn source analysis are applied
/// end-to-end in the generated OpenAPI document: controller bodies follow the MVC options
/// (<c>AddJsonOptions</c>) and never the HTTP options (<c>ConfigureHttpJsonOptions</c>).
/// </summary>
public class JsonOptionsIntegrationTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 24. AddJsonOptions with SnakeCaseLower → controller schema property names in snake_case
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithAddJsonOptions_SnakeCaseApplied()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Should().NotBeNull();

        // UserDto should be in the schemas with snake_case property names
        document.Components.Should().NotBeNull(
            because: "the document must have component schemas");
        document.Components!.Schemas.Should().NotBeNull().And.ContainKey("UserDto",
            because: "UserDto is referenced by the API and should appear in components");

        var userDtoSchemaA = document.Components.Schemas!["UserDto"];
        userDtoSchemaA.Should().BeOfType<OpenApiSchema>(
            because: "UserDto schema must be an inline schema, not a reference");
        var concreteSchemaA = (OpenApiSchema)userDtoSchemaA;
        concreteSchemaA.Properties.Should().NotBeNull(
            because: "UserDto must have properties defined in its schema");
        // DisplayName should be serialized as display_name
        concreteSchemaA.Properties!.Should().ContainKey("display_name",
            because: "Roslyn detected SnakeCaseLower → DisplayName → display_name");
        concreteSchemaA.Properties.Should().NotContainKey("displayName",
            because: "Roslyn detection overrides the default camelCase");
        concreteSchemaA.Properties.Should().NotContainKey("DisplayName",
            because: "PascalCase should not appear with SnakeCaseLower");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 25. No Roslyn options found → NamingPolicy CLI option is used
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithoutRoslynOptions_CliFlagUsed()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
            NamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Should().NotBeNull();

        document.Components.Should().NotBeNull(
            because: "the document must have component schemas");
        document.Components!.Schemas.Should().NotBeNull().And.ContainKey("UserDto");

        var schemaB = document.Components.Schemas!["UserDto"];
        schemaB.Should().BeOfType<OpenApiSchema>(
            because: "UserDto schema must be an inline schema, not a reference");
        var concreteB = (OpenApiSchema)schemaB;
        concreteB.Properties.Should().NotBeNull(
            because: "UserDto must have properties defined in its schema");
        concreteB.Properties!.Should().ContainKey("display_name",
            because: "CLI NamingPolicy = SnakeCaseLower should be applied when Roslyn finds nothing");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 26. Roslyn detected policy overrides CLI/option NamingPolicy
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_RoslynOverridesCliFlag()
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        // CLI option says CamelCase, but Roslyn should find SnakeCaseLower and win
        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
            NamingPolicy = JsonNamingPolicy.CamelCase,  // this should be overridden by Roslyn
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Should().NotBeNull();

        document.Components.Should().NotBeNull(
            because: "the document must have component schemas");
        document.Components!.Schemas.Should().NotBeNull().And.ContainKey("UserDto");

        var schemaC = document.Components.Schemas!["UserDto"];
        schemaC.Should().BeOfType<OpenApiSchema>(
            because: "UserDto schema must be an inline schema, not a reference");
        var concreteC = (OpenApiSchema)schemaC;
        concreteC.Properties.Should().NotBeNull(
            because: "UserDto must have properties defined in its schema");
        concreteC.Properties!.Should().ContainKey("display_name",
            because: "Roslyn detection of SnakeCaseLower wins over CLI NamingPolicy = CamelCase");
        concreteC.Properties.Should().NotContainKey("displayName",
            because: "Roslyn is the ground truth — camelCase must not win");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // §6. End-to-end pipeline: Roslyn short-name → registry → schema
    //
    // In degraded compilation (no reference assemblies for the converter type),
    // JsonOptionsExtractor falls back to the syntactic short name
    // "JsonStringEnumConverter".  JsonConverterRegistry.TryGet must then find
    // the entry via its short-name fallback (W2 fix), and SchemaGenerator must
    // apply the hint so that enums become string schemas in the final document.
    //
    // This test is the only path that exercises all three stages of the pipeline
    // together: Roslyn extraction → registry lookup → schema override.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_GlobalConverterShortName_EnumSchemaBecomesString()
    {
        // Program.cs uses a bare short name — no using-directive resolution possible
        // when the Roslyn compilation has no framework references.  The extractor
        // therefore captures the short type name "JsonStringEnumConverter".
        // The W2 fix in JsonConverterRegistry must resolve this to the correct hint,
        // and the SchemaGenerator must then turn all enum schemas into type=string.
        using var tempDir = new TempDirectory();
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        var options = new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        };

        var document = OpenApiDocumentBuilder.Build(options);

        document.Should().NotBeNull();
        document.Components!.Schemas.Should().NotBeNull();

        // UserDto is the response body type and appears in components/schemas.
        // Its Status property is of type UserStatus (enum).
        // With the global JsonStringEnumConverter applied, the inline Status schema
        // must be type=string — not the default type=integer.
        document.Components.Schemas.Should().ContainKey("UserDto",
            because: "UserDto is a response body type that must appear in components");

        var userDtoSchema = document.Components.Schemas!["UserDto"];
        userDtoSchema.Should().BeOfType<OpenApiSchema>();
        var dtoSchema = (OpenApiSchema)userDtoSchema;

        dtoSchema.Properties.Should().NotBeNull();
        // Default naming policy is CamelCase (no policy set), so "Status" → "status"
        dtoSchema.Properties!.Should().ContainKey("status",
            because: "UserDto.Status must appear in the schema (camelCase default)");

        var statusSchema = dtoSchema.Properties["status"] as OpenApiSchema;
        statusSchema.Should().NotBeNull(
            because: "the Status property schema must be a concrete inline schema");

        statusSchema!.Type.Should().Be(JsonSchemaType.String,
            because: "global JsonStringEnumConverter (resolved via short-name fallback) must override the default integer enum");
        statusSchema.Enum.Should().NotBeNullOrEmpty(
            because: "enum values should be emitted as string names");

        var values = statusSchema.Enum!
            .Select(n => n!.GetValue<string>())
            .ToList();
        values.Should().BeEquivalentTo("Active", "Suspended", "Banned", "Deleted");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Serialization contexts: controllers serialize with the MVC options only
    // ──────────────────────────────────────────────────────────────────────────

    private static OpenApiSchema UserDtoSchema(string programCs, JsonNamingPolicy? cliPolicy = null)
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "Program.cs"), programCs);

        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
            NamingPolicy = cliPolicy,
        });

        document.Components!.Schemas.Should().ContainKey("UserDto");
        var schema = document.Components.Schemas!["UserDto"].Should().BeOfType<OpenApiSchema>().Subject;
        schema.Properties.Should().NotBeNull();
        return schema;
    }

    [Fact]
    public void Build_OnlyConfigureHttpJsonOptions_ControllerSchemasKeepTheMvcDefault()
    {
        var schema = UserDtoSchema(
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        schema.Properties!.Should().ContainKey("displayName",
            because: "controllers serialize with the MVC options, whose default naming is camelCase");
        schema.Properties.Should().NotContainKey("display_name",
            because: "ConfigureHttpJsonOptions configures minimal APIs and IResult, never MVC");
    }

    [Fact]
    public void Build_OnlyConfigureHttpJsonOptions_CliNamingPolicyDoesNotReachControllers()
    {
        // Program.cs configures JSON options, so the CLI fallback does not apply and the
        // unconfigured MVC context keeps the ASP.NET Core default.
        var schema = UserDtoSchema(
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers();
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower;
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """,
            cliPolicy: JsonNamingPolicy.SnakeCaseLower);

        schema.Properties!.Should().ContainKey("displayName");
        schema.Properties.Should().NotContainKey("display_name");
        schema.Properties.Should().NotContainKey("display-name");
    }

    [Fact]
    public void Build_EqualOptionsInBothContexts_ControllerSchemasUseThem()
    {
        var schema = UserDtoSchema(
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower;
            });
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower;
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        schema.Properties!.Should().ContainKey("display-name");
    }

    [Fact]
    public void Build_DifferentOptionsInBothContexts_ControllerSchemasUseTheMvcOptions()
    {
        // ConfigureHttpJsonOptions comes last: in a merged view it would win.
        var schema = UserDtoSchema(
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower;
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        schema.Properties!.Should().ContainKey("display_name");
        var status = schema.Properties!["status"].Should().BeOfType<OpenApiSchema>().Subject;
        status.Type.Should().Be(JsonSchemaType.Integer,
            because: "the string enum converter is registered for the HTTP context only");
    }
}

/// <summary>
/// Builds ModernApi for 3.1 and 3.2 with a Program.cs that sets snake_case for the MVC context
/// only, and prepares schema validation against each document.
/// </summary>
public sealed class MvcContextConformanceFixture : IDisposable
{
    private readonly TempDirectory _source = new();

    public MvcContextConformanceFixture()
    {
        File.WriteAllText(
            Path.Combine(_source.Path, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllers().AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            });
            var app = builder.Build();
            app.MapControllers();
            app.Run();
            """);

        foreach (var version in new[] { OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2 })
        {
            var options = new OpenApiDocumentOptions
            {
                AssemblyPath   = TestPaths.ModernApiDll,
                XmlPath        = TestPaths.ModernApiXml,
                SourceRoot     = _source.Path,
                OpenApiVersion = version,
            };
            var document = VersionedDocumentHarness.BuildAndSerializeAsync(options, DocumentFormat.Json, CancellationToken.None)
                .GetAwaiter().GetResult();
            Documents[version] = document;
            Conformance[version] = SchemaConformance.For(document);
        }
    }

    public Dictionary<OpenApiSpecVersion, JsonNode> Documents { get; } = [];

    public Dictionary<OpenApiSpecVersion, SchemaConformance> Conformance { get; } = [];

    public void Dispose() => _source.Dispose();
}

/// <summary>
/// The schema of a controller body describes what MVC really writes with its own JSON options:
/// System.Text.Json output with the MVC options passes it, output with other naming does not.
/// </summary>
public class MvcContextConformanceTests(MvcContextConformanceFixture fixture) : IClassFixture<MvcContextConformanceFixture>
{
    private const string SchemasPrefix = "#/components/schemas/";

    public static TheoryData<OpenApiSpecVersion> Versions => [OpenApiSpecVersion.OpenApi3_1, OpenApiSpecVersion.OpenApi3_2];

    private static readonly OrderSummary Order = new() { OrderNumber = "A-17", ItemCount = 3, CustomerNote = "leave at door" };

    private string BodyComponent(OpenApiSpecVersion version)
    {
        var reference = fixture.Documents[version]["paths"]!["/contexts/mvc-order"]!["get"]!["responses"]!["200"]!
            ["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>();
        reference.Should().StartWith(SchemasPrefix);
        return reference[SchemasPrefix.Length..];
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void BodyWrittenWithMvcOptions_PassesTheBodySchema(OpenApiSpecVersion version)
    {
        var wire = StjWire.Serialize(Order, StjWire.Mvc(o => o.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower));
        wire!["order_number"].Should().NotBeNull(because: "the wire sample must be snake_case");

        var result = fixture.Conformance[version].ValidateComponent(BodyComponent(version), wire);

        result.IsValid.Should().BeTrue(because: string.Join("; ", result.Errors));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void BodyWrittenWithOtherNaming_FailsTheBodySchema(OpenApiSpecVersion version)
    {
        var wire = StjWire.Serialize(Order, StjWire.Http());
        wire!["orderNumber"].Should().NotBeNull(because: "the negative sample must be camelCase");

        var result = fixture.Conformance[version].ValidateComponent(BodyComponent(version), wire);

        result.IsValid.Should().BeFalse(
            because: "order_number and item_count are required, so a camelCase object is not the MVC wire");
    }
}
