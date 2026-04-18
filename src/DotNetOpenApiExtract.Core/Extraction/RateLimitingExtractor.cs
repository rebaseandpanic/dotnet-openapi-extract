using System.Reflection;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Rate limiting metadata extracted from <c>[EnableRateLimiting]</c> and
/// <c>[DisableRateLimiting]</c> attributes on a controller or action.
/// </summary>
public sealed record RateLimitInfo
{
    /// <summary>
    /// The rate-limiting policy name from <c>[EnableRateLimiting("policyName")]</c>.
    /// Empty string when <see cref="IsDisabled"/> is true.
    /// </summary>
    public required string PolicyName { get; init; }

    /// <summary>
    /// True when <c>[DisableRateLimiting]</c> is present on the action or controller,
    /// indicating that rate limiting is explicitly disabled for this endpoint.
    /// </summary>
    public bool IsDisabled { get; init; }
}

/// <summary>
/// Extracts rate-limiting information from <c>[EnableRateLimiting]</c> and
/// <c>[DisableRateLimiting]</c> attributes via reflection-only metadata.
/// </summary>
/// <remarks>
/// Precedence rules:
/// <list type="bullet">
///   <item><c>[DisableRateLimiting]</c> on the action or controller → <see cref="RateLimitInfo.IsDisabled"/> is true.</item>
///   <item><c>[EnableRateLimiting]</c> on the action → action-level policy is used.</item>
///   <item><c>[EnableRateLimiting]</c> on the controller only → controller-level policy applies to the action.</item>
///   <item>No attributes → returns <see langword="null"/>.</item>
/// </list>
/// When both action and controller carry <c>[EnableRateLimiting]</c>, the action-level attribute takes precedence.
/// </remarks>
public static class RateLimitingExtractor
{
    /// <summary>
    /// Extracts rate-limiting metadata for the given action.
    /// </summary>
    /// <param name="controller">The owning controller.</param>
    /// <param name="action">The action to inspect.</param>
    /// <returns>
    /// A <see cref="RateLimitInfo"/> when any rate-limiting attribute is present;
    /// <see langword="null"/> when neither the action nor the controller carries one.
    /// </returns>
    public static RateLimitInfo? Extract(ControllerInfo controller, ActionInfo action)
    {
        return Extract(
            action.Method.GetCustomAttributesData(),
            controller.Type.GetCustomAttributesData());
    }

    /// <summary>
    /// Extracts rate-limiting metadata using pre-fetched attribute lists.
    /// </summary>
    /// <param name="actionAttrs">Pre-fetched attribute data for the action method.</param>
    /// <param name="controllerAttrs">Pre-fetched attribute data for the controller type.</param>
    /// <returns>
    /// A <see cref="RateLimitInfo"/> when any rate-limiting attribute is present;
    /// <see langword="null"/> when neither list carries one.
    /// </returns>
    public static RateLimitInfo? Extract(
        IList<CustomAttributeData> actionAttrs,
        IList<CustomAttributeData> controllerAttrs)
    {
        // [DisableRateLimiting] on the action or controller wins immediately.
        if (AttributeHelper.HasAttribute(actionAttrs, AttributeHelper.Names.DisableRateLimiting)
            || AttributeHelper.HasAttribute(controllerAttrs, AttributeHelper.Names.DisableRateLimiting))
        {
            return new RateLimitInfo { PolicyName = string.Empty, IsDisabled = true };
        }

        // [EnableRateLimiting] on the action takes precedence over the controller.
        var actionAttr = AttributeHelper.GetAttribute(actionAttrs, AttributeHelper.Names.EnableRateLimiting);
        if (actionAttr != null)
        {
            var policy = AttributeHelper.GetConstructorArgument<string>(actionAttr, 0) ?? string.Empty;
            return new RateLimitInfo { PolicyName = policy };
        }

        // Fall back to controller-level [EnableRateLimiting].
        var controllerAttr = AttributeHelper.GetAttribute(controllerAttrs, AttributeHelper.Names.EnableRateLimiting);
        if (controllerAttr != null)
        {
            var policy = AttributeHelper.GetConstructorArgument<string>(controllerAttr, 0) ?? string.Empty;
            return new RateLimitInfo { PolicyName = policy };
        }

        return null;
    }
}
