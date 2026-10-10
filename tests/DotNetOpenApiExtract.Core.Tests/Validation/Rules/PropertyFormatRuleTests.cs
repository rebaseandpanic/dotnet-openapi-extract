using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Annotations;
using Xunit;
using CoreValidator = DotNetOpenApiExtract.Core.Validation.OpenApiValidator;
using ValidationContext = DotNetOpenApiExtract.Core.Validation.ValidationContext;
using ValidationViolation = DotNetOpenApiExtract.Core.Validation.ValidationViolation;

namespace DotNetOpenApiExtract.Core.Tests.Validation.Rules;

/// <summary>
/// <c>schema.property-format</c> accepts exactly the <c>[DataType]</c> table and the format priority:
/// <c>[SwaggerSchema(Format)]</c> → <c>[EmailAddress]</c> / <c>[Url]</c> / <c>[Phone]</c> →
/// <c>[DataType]</c> → the CLR type; a source without a format promises nothing.
/// </summary>
public sealed class PropertyFormatRuleTests
{
    private const string RuleId = "schema.property-format";

    // One property per [DataType] member; names follow the member.
    private sealed class DataTypes
    {
        [DataType(DataType.DateTime)] public string? DateTime { get; set; }
        [DataType(DataType.Date)] public string? Date { get; set; }
        [DataType(DataType.Time)] public string? Time { get; set; }
        [DataType(DataType.Duration)] public string? Duration { get; set; }
        [DataType(DataType.EmailAddress)] public string? EmailAddress { get; set; }
        [DataType(DataType.Password)] public string? Password { get; set; }
        [DataType(DataType.Url)] public string? Url { get; set; }
        [DataType(DataType.ImageUrl)] public string? ImageUrl { get; set; }
        [DataType(DataType.PhoneNumber)] public string? PhoneNumber { get; set; }
        [DataType(DataType.Upload)] public string? Upload { get; set; }
        [DataType(DataType.Custom)] public string? Custom { get; set; }
        [DataType(DataType.Currency)] public string? Currency { get; set; }
        [DataType(DataType.Text)] public string? Text { get; set; }
        [DataType(DataType.Html)] public string? Html { get; set; }
        [DataType(DataType.MultilineText)] public string? MultilineText { get; set; }
        [DataType(DataType.CreditCard)] public string? CreditCard { get; set; }
        [DataType(DataType.PostalCode)] public string? PostalCode { get; set; }
        [DataType("custom-name")] public string? CustomName { get; set; }
    }

    // Pairs of sources: the winner's format is the one the rule asks for.
    private sealed class Priorities
    {
        [SwaggerSchema(Format = "x-code")] public DateTime SwaggerOverType { get; set; }
        [SwaggerSchema(Format = "x-code"), EmailAddress] public string? SwaggerOverProfile { get; set; }
        [EmailAddress, DataType(DataType.Url)] public string? ProfileOverDataType { get; set; }
        [Url, DataType(DataType.EmailAddress)] public string? UrlOverDataType { get; set; }
        [Phone] public string? Phone { get; set; }
        [DataType(DataType.Date)] public DateTime DataTypeOverType { get; set; }
        [DataType(DataType.Text)] public DateTime TypeWhenDataTypeHasNoFormat { get; set; }
        [DataType(DataType.Text)] public Guid? NullableTypeWhenDataTypeHasNoFormat { get; set; }
        public DateOnly TypeOnly { get; set; }
    }

    private static IReadOnlyList<ValidationViolation> Check(Type fixture, string property, OpenApiSchema schema)
    {
        var doc = new OpenApiDocument
        {
            Info  = new OpenApiInfo { Title = "T", Version = "1" },
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["Fixture"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema> { [property] = schema },
                    },
                },
            },
        };
        var result = CoreValidator.Validate(doc, new ValidationContext
        {
            OpenApiSpecVersion = OpenApiSpecVersion.OpenApi3_1,
            TypeBySchemaId = new Dictionary<string, Type> { ["Fixture"] = fixture },
        });
        result.SkippedRules.Should().NotContain(RuleId);
        return result.Violations.Where(v => v.RuleId == RuleId).ToList();
    }

    private static OpenApiSchema Text(string? format) => new() { Type = JsonSchemaType.String, Format = format };

    /// <summary>The table: member → format, or no format.</summary>
    public static TheoryData<string, string?> Table => new()
    {
        { "dateTime", "date-time" },
        { "date", "date" },
        { "time", "time" },
        { "duration", "duration" },
        { "emailAddress", "email" },
        { "password", "password" },
        { "url", "uri" },
        { "imageUrl", "uri" },
        { "phoneNumber", "phone" },
        { "upload", "binary" },
        { "custom", null },
        { "currency", null },
        { "text", null },
        { "html", null },
        { "multilineText", null },
        { "creditCard", null },
        { "postalCode", null },
        { "customName", null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void DataTypeMember_FormatOfTheTable_NoViolation(string property, string? format)
    {
        Check(typeof(DataTypes), property, Text(format)).Should().BeEmpty();
    }

    /// <summary>A member with a format requires exactly it; a member without one requires nothing, so any format passes.</summary>
    [Theory]
    [MemberData(nameof(Table))]
    public void DataTypeMember_OtherFormat_ViolatesOnlyWhenTheTablePromisesOne(string property, string? format)
    {
        var violations = Check(typeof(DataTypes), property, Text("x-other"));

        violations.Should().HaveCount(format == null ? 0 : 1);
    }

    public static TheoryData<string, OpenApiSchema> Winners => new()
    {
        { "swaggerOverType", new OpenApiSchema { Type = JsonSchemaType.String, Format = "x-code" } },
        { "swaggerOverProfile", Text("x-code") },
        { "profileOverDataType", Text("email") },
        { "urlOverDataType", Text("uri") },
        { "phone", Text("phone") },
        { "dataTypeOverType", Text("date") },
        { "typeWhenDataTypeHasNoFormat", Text("date-time") },
        { "nullableTypeWhenDataTypeHasNoFormat", new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Format = "uuid" } },
        { "typeOnly", Text("date") },
    };

    [Theory]
    [MemberData(nameof(Winners))]
    public void Priority_WinnersFormat_NoViolation(string property, OpenApiSchema schema)
    {
        Check(typeof(Priorities), property, schema).Should().BeEmpty();
    }

    public static TheoryData<string, OpenApiSchema> Losers => new()
    {
        // The CLR type's format where [SwaggerSchema(Format)] wins.
        { "swaggerOverType", Text("date-time") },
        { "swaggerOverProfile", Text("email") },
        { "profileOverDataType", Text("uri") },
        { "dataTypeOverType", Text("date-time") },
        // [DataType] without a format leaves the type's format, which is then required.
        { "typeWhenDataTypeHasNoFormat", Text(null) },
    };

    [Theory]
    [MemberData(nameof(Losers))]
    public void Priority_LosersFormat_Violates(string property, OpenApiSchema schema)
    {
        Check(typeof(Priorities), property, schema).Should().ContainSingle()
            .Which.JsonPointer.Should().Be($"#/components/schemas/Fixture/properties/{property}");
    }

    [Fact]
    public void DataTypeEmailAddress_WithUri_Violates()
    {
        Check(typeof(DataTypes), "emailAddress", Text("uri")).Should().ContainSingle();
    }
}
