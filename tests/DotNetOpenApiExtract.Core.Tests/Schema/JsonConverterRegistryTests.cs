using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Schema;

/// <summary>
/// Unit tests for <see cref="JsonConverterRegistry"/> — verifies well-known converter
/// full names map to the correct <see cref="ConverterSchemaHint"/> values.
/// </summary>
public sealed class JsonConverterRegistryTests
{
    // =========================================================================
    // TryGet — known converters
    // =========================================================================

    [Fact]
    public void TryGet_JsonStringEnumConverter_ReturnsStringHint()
    {
        var hint = JsonConverterRegistry.TryGet(
            "System.Text.Json.Serialization.JsonStringEnumConverter");

        hint.Should().NotBeNull();
        hint!.SchemaType.Should().Be(JsonSchemaType.String);
        hint.Format.Should().BeNull();
    }

    [Fact]
    public void TryGet_IsoDateTimeConverter_ReturnsStringDateTime()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.IsoDateTimeConverter");

        hint.Should().NotBeNull();
        hint!.SchemaType.Should().Be(JsonSchemaType.String);
        hint.Format.Should().Be("date-time");
    }

    [Fact]
    public void TryGet_UnixDateTimeConverter_ReturnsIntegerInt64()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.UnixDateTimeConverter");

        hint.Should().NotBeNull();
        hint!.SchemaType.Should().Be(JsonSchemaType.Integer);
        hint.Format.Should().Be("int64");
        hint.Description.Should().Be("Unix timestamp (seconds)");
    }

    [Fact]
    public void TryGet_StringEnumConverter_Newtonsoft_ReturnsStringHint()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.StringEnumConverter");

        hint.Should().NotBeNull();
        hint!.SchemaType.Should().Be(JsonSchemaType.String);
    }

    [Fact]
    public void TryGet_JavaScriptDateTimeConverter_ReturnsStringDateTime()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.JavaScriptDateTimeConverter");

        hint.Should().NotBeNull();
        hint!.SchemaType.Should().Be(JsonSchemaType.String);
        hint.Format.Should().Be("date-time");
    }

    // =========================================================================
    // TryGet — unknown converter
    // =========================================================================

    [Fact]
    public void TryGet_UnknownConverter_ReturnsNull()
    {
        var hint = JsonConverterRegistry.TryGet(
            "My.Custom.ExoticConverter");

        hint.Should().BeNull();
    }

    [Fact]
    public void TryGet_NullOrEmpty_ReturnsNull()
    {
        JsonConverterRegistry.TryGet(string.Empty).Should().BeNull();
        JsonConverterRegistry.TryGet(null!).Should().BeNull();
    }

    // =========================================================================
    // TargetTypeFullNames
    // =========================================================================

    [Fact]
    public void TryGet_JsonStringEnumConverter_TargetsAnyEnum()
    {
        var hint = JsonConverterRegistry.TryGet(
            "System.Text.Json.Serialization.JsonStringEnumConverter");

        hint.Should().NotBeNull();
        hint!.TargetTypeFullNames.Should().Contain(JsonConverterRegistry.AnyEnumTarget);
    }

    [Fact]
    public void TryGet_IsoDateTimeConverter_TargetsDateTime()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.IsoDateTimeConverter");

        hint.Should().NotBeNull();
        hint!.TargetTypeFullNames.Should().Contain("System.DateTime");
        hint.TargetTypeFullNames.Should().Contain("System.DateTimeOffset");
    }

    [Fact]
    public void TryGet_UnixDateTimeConverter_TargetsDateTime()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.UnixDateTimeConverter");

        hint.Should().NotBeNull();
        hint!.TargetTypeFullNames.Should().Contain("System.DateTime");
        hint.TargetTypeFullNames.Should().Contain("System.DateTimeOffset");
    }

    // =========================================================================
    // Closed-generic normalisation
    // =========================================================================

    [Fact]
    public void TryGet_ClosedGenericJsonStringEnumConverter_ReturnsStringHint()
    {
        // MetadataLoadContext produces names like:
        // System.Text.Json.Serialization.JsonStringEnumConverter`1[[SomeEnum, ...]]
        const string closedGenericName =
            "System.Text.Json.Serialization.JsonStringEnumConverter`1" +
            "[[SampleApi.Models.Priority, SampleApi, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null]]";

        var hint = JsonConverterRegistry.TryGet(closedGenericName);

        hint.Should().NotBeNull();
        hint!.SchemaType.Should().Be(JsonSchemaType.String);
    }

    // =========================================================================
    // TryGetAny
    // =========================================================================

    [Fact]
    public void TryGetAny_FirstKnownWins()
    {
        var hint = JsonConverterRegistry.TryGetAny(
            "My.Unknown.Converter",
            "Newtonsoft.Json.Converters.IsoDateTimeConverter");

        hint.Should().NotBeNull();
        hint!.Format.Should().Be("date-time");
    }

    [Fact]
    public void TryGetAny_AllUnknown_ReturnsNull()
    {
        var hint = JsonConverterRegistry.TryGetAny(
            "My.Unknown.Converter",
            "Another.Unknown.Converter");

        hint.Should().BeNull();
    }

    // =========================================================================
    // AppliesToType
    // =========================================================================

    [Fact]
    public void AppliesToType_EnumConverter_AppliesToEnum()
    {
        var hint = JsonConverterRegistry.TryGet(
            "System.Text.Json.Serialization.JsonStringEnumConverter")!;

        JsonConverterRegistry.AppliesToType(hint, isEnum: true, targetTypeFullName: "MyEnum")
            .Should().BeTrue();
    }

    [Fact]
    public void AppliesToType_EnumConverter_DoesNotApplyToNonEnum()
    {
        var hint = JsonConverterRegistry.TryGet(
            "System.Text.Json.Serialization.JsonStringEnumConverter")!;

        JsonConverterRegistry.AppliesToType(hint, isEnum: false, targetTypeFullName: "System.String")
            .Should().BeFalse();
    }

    [Fact]
    public void AppliesToType_DateTimeConverter_AppliesToDateTime()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.IsoDateTimeConverter")!;

        JsonConverterRegistry.AppliesToType(hint, isEnum: false, targetTypeFullName: "System.DateTime")
            .Should().BeTrue();
    }

    [Fact]
    public void AppliesToType_DateTimeConverter_DoesNotApplyToEnum()
    {
        var hint = JsonConverterRegistry.TryGet(
            "Newtonsoft.Json.Converters.IsoDateTimeConverter")!;

        JsonConverterRegistry.AppliesToType(hint, isEnum: true, targetTypeFullName: "MyEnum")
            .Should().BeFalse();
    }

    [Fact]
    public void AppliesToType_HintWithEmptyTargets_AppliesToAny()
    {
        var hint = new ConverterSchemaHint
        {
            SchemaType = JsonSchemaType.String,
            TargetTypeFullNames = [],
        };

        JsonConverterRegistry.AppliesToType(hint, isEnum: false, targetTypeFullName: "Anything")
            .Should().BeTrue();
        JsonConverterRegistry.AppliesToType(hint, isEnum: true, targetTypeFullName: "MyEnum")
            .Should().BeTrue();
    }

    // =========================================================================
    // W2 — Short-name fallback lookup
    // =========================================================================

    [Fact]
    public void TryGet_ShortNameOnly_FindsUniqueMatch()
    {
        // When the semantic model in degraded compilation cannot resolve the FQN,
        // JsonOptionsExtractor may pass a bare short name. TryGet must find the entry.
        var hint = JsonConverterRegistry.TryGet("JsonStringEnumConverter");

        hint.Should().NotBeNull();
        hint!.SchemaType.Should().Be(JsonSchemaType.String);
    }

    [Fact]
    public void TryGet_FullNameWithWrongNamespace_DoesNotFallbackToShortName()
    {
        // A FQN input that contains '.' must NOT fall through to the short-name path,
        // even if the class-name portion matches a registry entry.
        var hint = JsonConverterRegistry.TryGet("Evil.Namespace.JsonStringEnumConverter");

        hint.Should().BeNull();
    }

    [Fact]
    public void TryGet_ShortNameOnly_NoMatch_ReturnsNull()
    {
        // A short name that does not appear in the registry returns null.
        var hint = JsonConverterRegistry.TryGet("CompletelyUnknownConverter");

        hint.Should().BeNull();
    }
}
