using Microsoft.AspNetCore.Mvc.Routing;

namespace ModernApi.Attributes;

/// <summary>
/// A custom HTTP method attribute. Its methods are passed to the base constructor in code, so they
/// are not visible in metadata; the extractor must not guess "QUERY" from the attribute's name.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpQueryAttribute : HttpMethodAttribute
{
    /// <summary>Creates the attribute for the given route template.</summary>
    /// <param name="template">Route template.</param>
    public HttpQueryAttribute(string template)
        : base(["QUERY"], template)
    {
    }
}
