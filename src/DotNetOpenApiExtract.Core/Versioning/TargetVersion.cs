using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>
/// The single place that decides which OpenAPI versions a build may target, how the
/// CLI spelling (<c>3.0</c>, <c>3.1</c>, <c>3.2</c>) maps to <see cref="OpenApiSpecVersion"/>,
/// and which version a build with validation validates against.
/// </summary>
internal static class TargetVersion
{
    /// <summary>The version used when none is given.</summary>
    public const OpenApiSpecVersion Default = OpenApiSpecVersion.OpenApi3_0;

    private const string Accepted = "3.0, 3.1, or 3.2";

    /// <summary>Whether <paramref name="version"/> is a version a document can be built for.</summary>
    public static bool IsSupported(OpenApiSpecVersion version) =>
        version is OpenApiSpecVersion.OpenApi3_0
            or OpenApiSpecVersion.OpenApi3_1
            or OpenApiSpecVersion.OpenApi3_2;

    /// <summary>
    /// Throws <see cref="OpenApiConfigurationException"/> unless <paramref name="version"/>
    /// is supported. <paramref name="setting"/> names the option in the message.
    /// </summary>
    public static void EnsureSupported(OpenApiSpecVersion version, string setting)
    {
        if (!IsSupported(version))
            throw new OpenApiConfigurationException(
                $"Unsupported OpenAPI version {Describe(version)} in {setting}. Use {Accepted}.");
    }

    /// <summary>
    /// Parses the CLI spelling of a version. Anything other than <c>3.0</c>, <c>3.1</c> or
    /// <c>3.2</c> is a <see cref="OpenApiConfigurationException"/>, never a silent default.
    /// </summary>
    public static OpenApiSpecVersion Parse(string? text) => text switch
    {
        "3.0" => OpenApiSpecVersion.OpenApi3_0,
        "3.1" => OpenApiSpecVersion.OpenApi3_1,
        "3.2" => OpenApiSpecVersion.OpenApi3_2,
        _ => throw new OpenApiConfigurationException(
            $"Unknown OpenAPI version '{text}'. Use {Accepted}."),
    };

    /// <summary>
    /// The version a build with validation validates against: the build version when the
    /// validation context leaves it unset; a different explicit value is a configuration error.
    /// </summary>
    public static OpenApiSpecVersion ResolveValidationVersion(
        OpenApiSpecVersion buildVersion, OpenApiSpecVersion? validationVersion)
    {
        if (validationVersion is { } explicitVersion && explicitVersion != buildVersion)
            throw new OpenApiConfigurationException(
                $"The build targets OpenAPI {Describe(buildVersion)} but the validation context " +
                $"names OpenAPI {Describe(explicitVersion)}. A document is built, validated and " +
                "serialized for one version: set the same version in both, or leave it unset " +
                "in the validation context.");

        return buildVersion;
    }

    /// <summary>The short spelling of <paramref name="version"/> (<c>3.0</c>, <c>3.1</c>, …) used in messages.</summary>
    public static string Describe(OpenApiSpecVersion version) => version switch
    {
        OpenApiSpecVersion.OpenApi2_0 => "2.0",
        OpenApiSpecVersion.OpenApi3_0 => "3.0",
        OpenApiSpecVersion.OpenApi3_1 => "3.1",
        OpenApiSpecVersion.OpenApi3_2 => "3.2",
        _ => $"value {(int)version}",
    };
}
