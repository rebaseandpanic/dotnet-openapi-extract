using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Schema;
using DotNetOpenApiExtract.Core.Validation;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Versioning;

/// <summary>
/// The target-version contract on every public Core surface: only 3.0, 3.1 and 3.2 are accepted,
/// anything else is a configuration error raised before the assembly is loaded, and a build with
/// validation cannot name two different explicit versions.
/// </summary>
public class TargetVersionContractTests
{
    /// <summary>
    /// Does not exist: a configuration error must win over the load error, which a supported
    /// version reaches (see the control cases below).
    /// </summary>
    private static readonly string MissingAssembly =
        Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "Missing.dll");

    public static TheoryData<OpenApiSpecVersion> UnsupportedVersions =>
    [
        OpenApiSpecVersion.OpenApi2_0,
        (OpenApiSpecVersion)42,
    ];

    public static TheoryData<OpenApiSpecVersion> SupportedVersions =>
    [
        OpenApiSpecVersion.OpenApi3_0,
        OpenApiSpecVersion.OpenApi3_1,
        OpenApiSpecVersion.OpenApi3_2,
    ];

    [Theory]
    [MemberData(nameof(UnsupportedVersions))]
    public void Build_UnsupportedVersion_ThrowsConfigurationErrorBeforeLoading(OpenApiSpecVersion version)
    {
        var act = () => OpenApiDocumentBuilder.Build(Options(version));

        act.Should().ThrowExactly<OpenApiConfigurationException>();
    }

    [Theory]
    [MemberData(nameof(UnsupportedVersions))]
    public void BuildWithValidation_UnsupportedVersion_ThrowsConfigurationErrorBeforeLoading(
        OpenApiSpecVersion version)
    {
        var act = () => OpenApiDocumentBuilder.BuildWithValidation(
            Options(version), new ValidationContext(), out _);

        act.Should().ThrowExactly<OpenApiConfigurationException>();
    }

    [Theory]
    [MemberData(nameof(UnsupportedVersions))]
    public void SchemaGenerator_UnsupportedVersion_ThrowsConfigurationError(OpenApiSpecVersion version)
    {
        var act = () => new SchemaGenerator(new SchemaOptions { OpenApiVersion = version });

        act.Should().ThrowExactly<OpenApiConfigurationException>();
    }

    [Theory]
    [MemberData(nameof(SupportedVersions))]
    public void SchemaGenerator_SupportedVersion_IsAccepted(OpenApiSpecVersion version)
    {
        var act = () => new SchemaGenerator(new SchemaOptions { OpenApiVersion = version });

        act.Should().NotThrow();
    }

    [Fact]
    public void BuildWithValidation_DifferentExplicitVersions_ThrowsConfigurationErrorBeforeLoading()
    {
        var act = () => OpenApiDocumentBuilder.BuildWithValidation(
            Options(OpenApiSpecVersion.OpenApi3_1),
            new ValidationContext { OpenApiSpecVersion = OpenApiSpecVersion.OpenApi3_2 },
            out _);

        act.Should().ThrowExactly<OpenApiConfigurationException>();
    }

    /// <summary>
    /// Control: an accepted configuration gets past the checks and fails only on the missing
    /// assembly, so the cases above fail on the version, not on something else.
    /// </summary>
    [Theory]
    [InlineData(OpenApiSpecVersion.OpenApi3_1, null)]
    [InlineData(OpenApiSpecVersion.OpenApi3_2, OpenApiSpecVersion.OpenApi3_2)]
    public void BuildWithValidation_SameOrUnsetValidationVersion_ReachesAssemblyLoading(
        OpenApiSpecVersion buildVersion, OpenApiSpecVersion? validationVersion)
    {
        var act = () => OpenApiDocumentBuilder.BuildWithValidation(
            Options(buildVersion),
            new ValidationContext { OpenApiSpecVersion = validationVersion },
            out _);

        act.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [MemberData(nameof(SupportedVersions))]
    public void Build_SupportedVersion_ReachesAssemblyLoading(OpenApiSpecVersion version)
    {
        var act = () => OpenApiDocumentBuilder.Build(Options(version));

        act.Should().Throw<FileNotFoundException>();
    }

    private static OpenApiDocumentOptions Options(OpenApiSpecVersion version) => new()
    {
        AssemblyPath   = MissingAssembly,
        OpenApiVersion = version,
    };
}
