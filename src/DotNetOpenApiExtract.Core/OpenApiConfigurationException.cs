namespace DotNetOpenApiExtract.Core;

/// <summary>
/// Thrown when the configuration of a build is invalid or contradicts itself: an unsupported
/// target OpenAPI version, or different explicit versions for building and validating.
/// It is raised before the target assembly is loaded. The CLI reports it with exit code 2.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so existing <c>catch</c> blocks keep
/// working; catch this type to tell a configuration error from other failures.
/// </remarks>
public sealed class OpenApiConfigurationException : InvalidOperationException
{
    /// <summary>Initializes a new instance with a message that names the invalid setting.</summary>
    /// <param name="message">What is wrong and which values are accepted.</param>
    public OpenApiConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and the exception that caused it.</summary>
    /// <param name="message">What is wrong and which values are accepted.</param>
    /// <param name="innerException">The underlying failure.</param>
    public OpenApiConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
