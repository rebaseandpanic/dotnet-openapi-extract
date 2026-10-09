using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;
using StjNamingPolicy = System.Text.Json.JsonNamingPolicy;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// camelCase names follow System.Text.Json's <c>JsonNamingPolicy.CamelCase</c>, including names that
/// start with an acronym (<c>IOStatus</c> → <c>ioStatus</c>), for property names and enum members.
/// </summary>
public class CamelCaseAcronymTests
{
    public sealed class Names
    {
        public int Plain { get; set; }
        public int IOStatus { get; set; }
        public int XMLHttpRequest { get; set; }
        public int HTML { get; set; }
        public int HTTPSEnabled { get; set; }
        public int AB { get; set; }
    }

    public enum Members { Plain, IOStatus, XMLHttpRequest, HTML, HTTPSEnabled, AB }

    public static TheoryData<string> AllNames => [.. Enum.GetNames<Members>()];

    [Theory]
    [MemberData(nameof(AllNames))]
    public void PropertyName_IsWhatSystemTextJsonWrites(string name)
    {
        var generator = new SchemaGenerator(new SchemaOptions { NamingPolicy = JsonNamingPolicy.CamelCase });
        generator.GenerateSchema(typeof(Names));

        generator.Schemas[nameof(Names)].Properties!.Keys.Should().Contain(StjNamingPolicy.CamelCase.ConvertName(name));
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void EnumMember_UnderACamelCaseConverter_IsWhatSystemTextJsonWrites(string name)
    {
        var generator = new SchemaGenerator(new SchemaOptions
        {
            GlobalConverterTypeNames = ["System.Text.Json.Serialization.JsonStringEnumConverter"],
            GlobalConverterEnumNamingPolicies = [JsonNamingPolicy.CamelCase],
        });
        var json = JsonNode.Parse(generator.GenerateSchema(typeof(Members))
            .SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!;
        var values = json["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

        values[(int)Enum.Parse<Members>(name)].Should().Be(StjNamingPolicy.CamelCase.ConvertName(name));
    }
}
