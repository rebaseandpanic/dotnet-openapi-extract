// Stub attribute types that mirror the Asp.Versioning NuGet package API.
// Using the exact same full-qualified names ensures MetadataLoadContext
// attribute matching in ApiVersionExtractor works without pulling in the
// real Asp.Versioning package.
namespace Asp.Versioning;

/// <summary>
/// Declares the API version(s) supported by a controller or action.
/// Mirrors <c>Asp.Versioning.ApiVersionAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ApiVersionAttribute : Attribute
{
    /// <summary>The version string (e.g. "1.0", "2.0").</summary>
    public string Version { get; }

    /// <summary>Initialises the attribute with a string version specifier.</summary>
    /// <param name="version">Version string, e.g. "1.0".</param>
    public ApiVersionAttribute(string version) { Version = version; }

    /// <summary>Initialises the attribute with a major.minor integer pair.</summary>
    /// <param name="major">Major version number.</param>
    /// <param name="minor">Minor version number.</param>
    public ApiVersionAttribute(int major, int minor) { Version = $"{major}.{minor}"; }

    /// <summary>Initialises the attribute with a double value.</summary>
    /// <param name="version">Version as a double (e.g. 1.0).</param>
    public ApiVersionAttribute(double version) { Version = version.ToString("0.0"); }

    /// <summary>Initialises the attribute with a major.minor integer pair and a prerelease status suffix.</summary>
    /// <param name="major">Major version number.</param>
    /// <param name="minor">Minor version number.</param>
    /// <param name="status">Prerelease status label (e.g. "beta", "rc1").</param>
    public ApiVersionAttribute(int major, int minor, string status) { Version = $"{major}.{minor}-{status}"; }

    /// <summary>Initialises the attribute with a double value and a prerelease status suffix.</summary>
    /// <param name="version">Version as a double (e.g. 1.0).</param>
    /// <param name="status">Prerelease status label (e.g. "alpha", "rc").</param>
    public ApiVersionAttribute(double version, string status)
    {
        var formatted = version.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
        Version = string.IsNullOrEmpty(status) ? formatted : $"{formatted}-{status}";
    }
}

/// <summary>
/// Maps an action to a specific API version declared on the controller.
/// Mirrors <c>Asp.Versioning.MapToApiVersionAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class MapToApiVersionAttribute : Attribute
{
    /// <summary>The target version string.</summary>
    public string Version { get; }

    /// <summary>Initialises the attribute with the target version string.</summary>
    /// <param name="version">Version string, e.g. "2.0".</param>
    public MapToApiVersionAttribute(string version) { Version = version; }
}

/// <summary>
/// Marks a controller or action as version-neutral (applies to all API versions).
/// Mirrors <c>Asp.Versioning.ApiVersionNeutralAttribute</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApiVersionNeutralAttribute : Attribute { }
