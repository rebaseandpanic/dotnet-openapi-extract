using System.Reflection;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Response caching metadata extracted from <c>[ResponseCache]</c> or <c>[OutputCache]</c>
/// attributes on a controller or action.
/// </summary>
public sealed record ResponseCacheInfo
{
    /// <summary>
    /// Cache duration in seconds, or <see langword="null"/> when not specified.
    /// Maps to <c>max-age</c> in <c>Cache-Control</c>.
    /// </summary>
    public int? DurationSeconds { get; init; }

    /// <summary>
    /// Response cache location string: <c>"Any"</c>, <c>"Client"</c>, or <c>"None"</c>.
    /// <see langword="null"/> when the attribute did not specify a location.
    /// Derived from the <c>ResponseCacheLocation</c> enum (Any=0, Client=1, None=2).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// True when the <c>NoStore</c> property is set to <see langword="true"/>
    /// on the attribute. Adds <c>no-store</c> to the <c>Cache-Control</c> description.
    /// </summary>
    public bool NoStore { get; init; }

    /// <summary>
    /// The value of the <c>VaryByHeader</c> property, or <see langword="null"/> when absent.
    /// </summary>
    public string? VaryByHeader { get; init; }

    /// <summary>
    /// Source attribute that provided the caching metadata:
    /// <c>"ResponseCache"</c> or <c>"OutputCache"</c>.
    /// </summary>
    public string? Source { get; init; }
}

/// <summary>
/// Extracts response caching information from <c>[ResponseCache]</c> and <c>[OutputCache]</c>
/// attributes via reflection-only metadata.
/// </summary>
/// <remarks>
/// Precedence rules:
/// <list type="bullet">
///   <item>Action-level attribute takes precedence over controller-level.</item>
///   <item><c>[ResponseCache]</c> and <c>[OutputCache]</c> are both inspected; the first found wins (action before controller, ResponseCache before OutputCache).</item>
///   <item>No attributes → returns <see langword="null"/>.</item>
/// </list>
/// Enum values for <c>ResponseCacheLocation</c> are read as integers:
/// Any=0, Client=1, None=2.
/// </remarks>
public static class ResponseCachingExtractor
{
    /// <summary>
    /// Extracts response caching metadata for the given action.
    /// </summary>
    /// <param name="controller">The owning controller.</param>
    /// <param name="action">The action to inspect.</param>
    /// <returns>
    /// A <see cref="ResponseCacheInfo"/> when a caching attribute is present;
    /// <see langword="null"/> when neither the action nor the controller carries one.
    /// </returns>
    public static ResponseCacheInfo? Extract(ControllerInfo controller, ActionInfo action)
    {
        return Extract(
            action.Method.GetCustomAttributesData(),
            controller.Type.GetCustomAttributesData());
    }

    /// <summary>
    /// Extracts response caching metadata using pre-fetched attribute lists.
    /// </summary>
    /// <param name="actionAttrs">Pre-fetched attribute data for the action method.</param>
    /// <param name="controllerAttrs">Pre-fetched attribute data for the controller type.</param>
    /// <returns>
    /// A <see cref="ResponseCacheInfo"/> when a caching attribute is present;
    /// <see langword="null"/> when neither list carries one.
    /// </returns>
    public static ResponseCacheInfo? Extract(
        IList<CustomAttributeData> actionAttrs,
        IList<CustomAttributeData> controllerAttrs)
    {
        // Action-level [ResponseCache] takes highest precedence.
        var rcAction = AttributeHelper.GetAttribute(actionAttrs, AttributeHelper.Names.ResponseCache);
        if (rcAction != null)
            return ParseResponseCache(rcAction);

        // Action-level [OutputCache].
        var ocAction = AttributeHelper.GetAttribute(actionAttrs, AttributeHelper.Names.OutputCache);
        if (ocAction != null)
            return ParseOutputCache(ocAction);

        // Controller-level [ResponseCache].
        var rcController = AttributeHelper.GetAttribute(controllerAttrs, AttributeHelper.Names.ResponseCache);
        if (rcController != null)
            return ParseResponseCache(rcController);

        // Controller-level [OutputCache].
        var ocController = AttributeHelper.GetAttribute(controllerAttrs, AttributeHelper.Names.OutputCache);
        if (ocController != null)
            return ParseOutputCache(ocController);

        return null;
    }

    // -------------------------------------------------------------------------
    // Attribute parsers
    // -------------------------------------------------------------------------

    private static ResponseCacheInfo ParseResponseCache(System.Reflection.CustomAttributeData attr)
    {
        var duration = AttributeHelper.GetNamedArgument<int?>(attr, "Duration");
        var noStore  = AttributeHelper.GetNamedArgument<bool?>(attr, "NoStore") == true;
        var varyBy   = AttributeHelper.GetNamedArgument<string?>(attr, "VaryByHeader");

        // Location is an enum (ResponseCacheLocation); MetadataLoadContext exposes it as int.
        string? location = null;
        var locationArg = attr.NamedArguments
            .FirstOrDefault(a => a.MemberName == "Location");
        if (locationArg.TypedValue.Value is int locationInt)
        {
            location = locationInt switch
            {
                0 => "Any",
                1 => "Client",
                2 => "None",
                _ => null,
            };
        }

        return new ResponseCacheInfo
        {
            DurationSeconds = duration,
            Location        = location,
            NoStore         = noStore,
            VaryByHeader    = varyBy,
            Source          = "ResponseCache",
        };
    }

    private static ResponseCacheInfo ParseOutputCache(System.Reflection.CustomAttributeData attr)
    {
        var duration = AttributeHelper.GetNamedArgument<int?>(attr, "Duration");

        return new ResponseCacheInfo
        {
            DurationSeconds = duration,
            Source          = "OutputCache",
        };
    }
}
