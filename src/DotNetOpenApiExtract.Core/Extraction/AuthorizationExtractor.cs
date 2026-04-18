using System.Reflection;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Authorization metadata extracted from <c>[Authorize]</c> and <c>[AllowAnonymous]</c>
/// attributes on a controller or action.
/// </summary>
public sealed class AuthorizationInfo
{
    /// <summary>
    /// True if <c>[AllowAnonymous]</c> is present on the action or on the controller.
    /// When true, <see cref="RequiresAuthorization"/> is always false.
    /// </summary>
    public bool IsAnonymous { get; init; }

    /// <summary>
    /// True if <c>[Authorize]</c> is present (on the action or on the controller)
    /// and <c>[AllowAnonymous]</c> is NOT present on the action.
    /// </summary>
    public bool RequiresAuthorization { get; init; }

    /// <summary>
    /// Scheme names from <c>[Authorize(AuthenticationSchemes = "Bearer,Cookie")]</c>,
    /// or <see langword="null"/> if the attribute did not specify schemes.
    /// </summary>
    public IReadOnlyList<string>? AuthenticationSchemes { get; init; }

    /// <summary>
    /// Policy names from <c>[Authorize(Policy = "AdminOnly")]</c>,
    /// or <see langword="null"/> if no policy was specified.
    /// </summary>
    public IReadOnlyList<string>? Policies { get; init; }
}

/// <summary>
/// Extracts authorization information from controller/action <c>[Authorize]</c>
/// and <c>[AllowAnonymous]</c> attributes via reflection-only metadata.
/// </summary>
public static class AuthorizationExtractor
{
    /// <summary>
    /// Extracts authorization information for the given action.
    /// </summary>
    /// <remarks>
    /// Precedence rules:
    /// <list type="bullet">
    ///   <item><c>[AllowAnonymous]</c> on the action always wins — <see cref="AuthorizationInfo.IsAnonymous"/> is true.</item>
    ///   <item><c>[AllowAnonymous]</c> on the controller applies to all actions unless overridden by <c>[Authorize]</c> on the action.</item>
    ///   <item><c>[Authorize]</c> on the action or controller sets <see cref="AuthorizationInfo.RequiresAuthorization"/> to true.</item>
    ///   <item>When both controller and action have <c>[Authorize]</c>, action-level attributes take priority for scheme/policy details.</item>
    /// </list>
    /// </remarks>
    public static AuthorizationInfo Extract(ControllerInfo controller, ActionInfo action)
    {
        return Extract(
            action.Method.GetCustomAttributesData(),
            controller.Type.GetCustomAttributesData());
    }

    /// <summary>
    /// Extracts authorization information using pre-fetched attribute lists.
    /// </summary>
    /// <param name="actionAttrs">Pre-fetched attribute data for the action method.</param>
    /// <param name="controllerAttrs">Pre-fetched attribute data for the controller type.</param>
    public static AuthorizationInfo Extract(
        IList<CustomAttributeData> actionAttrs,
        IList<CustomAttributeData> controllerAttrs)
    {
        bool actionHasAllowAnon      = AttributeHelper.HasAttribute(actionAttrs, AttributeHelper.Names.AllowAnonymous);
        bool controllerHasAllowAnon  = AttributeHelper.HasAttribute(controllerAttrs, AttributeHelper.Names.AllowAnonymous);
        bool actionHasAuthorize      = AttributeHelper.HasAttribute(actionAttrs, AttributeHelper.Names.Authorize);
        bool controllerHasAuthorize  = AttributeHelper.HasAttribute(controllerAttrs, AttributeHelper.Names.Authorize);

        // [AllowAnonymous] on the action always wins.
        if (actionHasAllowAnon)
            return new AuthorizationInfo { IsAnonymous = true, RequiresAuthorization = false };

        // [AllowAnonymous] on controller applies unless action has [Authorize].
        if (controllerHasAllowAnon && !actionHasAuthorize)
            return new AuthorizationInfo { IsAnonymous = true, RequiresAuthorization = false };

        // No authorization anywhere.
        if (!actionHasAuthorize && !controllerHasAuthorize)
            return new AuthorizationInfo { IsAnonymous = false, RequiresAuthorization = false };

        // [Authorize] is present — collect details from action-level attributes first,
        // then fall back to controller-level. Use HashSet for dedup, List for ordered output.
        var seenSchemes  = new HashSet<string>(StringComparer.Ordinal);
        var seenPolicies = new HashSet<string>(StringComparer.Ordinal);
        var schemes  = new List<string>();
        var policies = new List<string>();

        foreach (var attr in AttributeHelper.GetAttributes(actionAttrs, AttributeHelper.Names.Authorize))
            CollectAuthorizeAttr(attr, schemes, policies, seenSchemes, seenPolicies);

        foreach (var attr in AttributeHelper.GetAttributes(controllerAttrs, AttributeHelper.Names.Authorize))
            CollectAuthorizeAttr(attr, schemes, policies, seenSchemes, seenPolicies);

        return new AuthorizationInfo
        {
            IsAnonymous = false,
            RequiresAuthorization = true,
            AuthenticationSchemes = schemes.Count > 0 ? schemes : null,
            Policies              = policies.Count > 0 ? policies : null,
        };
    }

    private static void CollectAuthorizeAttr(
        CustomAttributeData attr,
        List<string> schemes,
        List<string> policies,
        HashSet<string> seenSchemes,
        HashSet<string> seenPolicies)
    {
        // Constructor arg 0: string policy  →  [Authorize("PolicyName")]
        var ctorPolicy = AttributeHelper.GetConstructorArgument<string>(attr, 0);
        if (!string.IsNullOrWhiteSpace(ctorPolicy) && seenPolicies.Add(ctorPolicy!))
            policies.Add(ctorPolicy!);

        // Named arg: Policy = "PolicyName"
        var namedPolicy = AttributeHelper.GetNamedArgument<string>(attr, "Policy");
        if (!string.IsNullOrWhiteSpace(namedPolicy) && seenPolicies.Add(namedPolicy!))
            policies.Add(namedPolicy!);

        // Named arg: AuthenticationSchemes = "Bearer,Cookie"
        var schemesStr = AttributeHelper.GetNamedArgument<string>(attr, "AuthenticationSchemes");
        if (!string.IsNullOrWhiteSpace(schemesStr))
        {
            foreach (var scheme in schemesStr!.Split(',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (seenSchemes.Add(scheme))
                    schemes.Add(scheme);
            }
        }
    }
}
