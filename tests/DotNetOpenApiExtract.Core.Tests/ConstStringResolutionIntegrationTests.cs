using AwesomeAssertions;
using DotNetOpenApiExtract.Core;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// End-to-end tests for the README contract "In-project <c>const string</c> values via
/// <c>SemanticModel.GetConstantValue</c>": a name passed to a Program.cs registration as
/// an in-project <c>const string</c> must reach the spec as the constant's literal value.
/// The compilation is built by the product path (<c>SourceRoot</c> →
/// <c>SourceCompiler</c>), not by a hand-assembled reference list.
/// </summary>
public sealed class ConstStringResolutionIntegrationTests
{
    /// <summary>Where the resolved constant is expected to show up in the document.</summary>
    public enum SpecLocation
    {
        /// <summary>Key in <c>components.securitySchemes</c>.</summary>
        SecuritySchemeName,

        /// <summary>Header name on every response (global middleware header).</summary>
        ResponseHeaderName,

        /// <summary>Scheme referenced by a document-level <c>security</c> requirement.</summary>
        GlobalSecurityRequirementScheme,
    }

    /// <summary>
    /// Constants live in their own file, as they do in real projects; the scheme and
    /// header values are deliberately different from any default the extractors fall
    /// back to (e.g. <c>AddJwtBearer</c> without a name yields <c>Bearer</c>).
    /// </summary>
    private const string ConstsFile =
        """
        static class Consts
        {
            public const string ApiKeyScheme = "ApiKeyFromConst";
            public const string JwtScheme = "JwtFromConst";
            public const string AppendedHeader = "X-Appended-From-Const";
            public const string IndexedHeader = "X-Indexed-From-Const";
            public const string RequirementScheme = "RequirementFromConst";
            public const string LambdaRequirementScheme = "LambdaRequirementFromConst";
            public const string LegacyRequirementScheme = "LegacyRequirementFromConst";
        }
        """;

    public static TheoryData<string, SpecLocation, string> Registrations => new()
    {
        {
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition(Consts.ApiKeyScheme, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                });
            });
            builder.Build().Run();
            """,
            SpecLocation.SecuritySchemeName,
            "ApiKeyFromConst"
        },
        {
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddAuthentication().AddJwtBearer(Consts.JwtScheme, o => { });
            builder.Build().Run();
            """,
            SpecLocation.SecuritySchemeName,
            "JwtFromConst"
        },
        {
            """
            var app = WebApplication.CreateBuilder(args).Build();
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Append(Consts.AppendedHeader, "v");
                await next();
            });
            app.Run();
            """,
            SpecLocation.ResponseHeaderName,
            "X-Appended-From-Const"
        },
        {
            """
            var app = WebApplication.CreateBuilder(args).Build();
            app.Use(async (context, next) =>
            {
                context.Response.Headers[Consts.IndexedHeader] = "v";
                await next();
            });
            app.Run();
            """,
            SpecLocation.ResponseHeaderName,
            "X-Indexed-From-Const"
        },
        // Requirement cases declare the scheme as well: a requirement on an undeclared
        // scheme is omitted from the spec by design, which would mask the property tested.
        {
            // Microsoft.OpenApi 2.x+: scheme reference constructor argument.
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition(Consts.RequirementScheme, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { new OpenApiSecuritySchemeReference(Consts.RequirementScheme), [] }
                });
            });
            builder.Build().Run();
            """,
            SpecLocation.GlobalSecurityRequirementScheme,
            "RequirementFromConst"
        },
        {
            // Swashbuckle 10 lambda-factory form: the host document is the second argument.
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition(Consts.LambdaRequirementScheme, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                });
                c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(Consts.LambdaRequirementScheme, doc)] = []
                });
            });
            builder.Build().Run();
            """,
            SpecLocation.GlobalSecurityRequirementScheme,
            "LambdaRequirementFromConst"
        },
        {
            // Microsoft.OpenApi 1.x / classic Swashbuckle: OpenApiReference.Id.
            """
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition(Consts.LegacyRequirementScheme, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-Api-Key",
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = Consts.LegacyRequirementScheme,
                            }
                        },
                        new List<string>()
                    }
                });
            });
            builder.Build().Run();
            """,
            SpecLocation.GlobalSecurityRequirementScheme,
            "LegacyRequirementFromConst"
        },
    };

    [Theory]
    [MemberData(nameof(Registrations))]
    public void Build_ConstStringArgumentInProgramCs_ResolvedToLiteralValueInSpec(
        string programSource, SpecLocation location, string expectedName)
    {
        using var tempDir = new TempDirectory();
        File.WriteAllText(Path.Combine(tempDir.Path, "Program.cs"), programSource);
        File.WriteAllText(Path.Combine(tempDir.Path, "Consts.cs"), ConstsFile);
        File.WriteAllText(
            Path.Combine(tempDir.Path, "Dummy.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = TestPaths.SampleApiDll,
            XmlPath      = TestPaths.SampleApiXml,
            SourceRoot   = tempDir.Path,
        });

        switch (location)
        {
            case SpecLocation.SecuritySchemeName:
                document.Components?.SecuritySchemes.Should().NotBeNull();
                document.Components!.SecuritySchemes!.Keys.Should().Contain(expectedName);
                break;

            case SpecLocation.ResponseHeaderName:
                var responses = document.Paths!.Values
                    .OfType<OpenApiPathItem>()
                    .Where(p => p.Operations != null)
                    .SelectMany(p => p.Operations!.Values)
                    .Where(o => o.Responses != null)
                    .SelectMany(o => o.Responses!.Values)
                    .OfType<OpenApiResponse>()
                    .ToList();
                responses.Should().NotBeEmpty();
                responses.Should().AllSatisfy(r =>
                    r.Headers.Should().NotBeNull().And.ContainKey(expectedName));
                break;

            case SpecLocation.GlobalSecurityRequirementScheme:
                document.Security.Should().NotBeNullOrEmpty();
                document.Security!
                    .SelectMany(requirement => requirement.Keys)
                    .Select(schemeReference => schemeReference.Reference.Id)
                    .Should().Contain(expectedName);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(location), location, null);
        }
    }
}
