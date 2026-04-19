using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Validation.Rules;

/// <summary>
/// Rule: <c>operation.security</c>
/// If an operation is protected by <c>[Authorize]</c> (no <c>[AllowAnonymous]</c>) and there is no
/// global security defined, the operation must carry its own non-null/non-empty <c>security</c> field.
/// <para>
/// In standalone mode (no CLR bindings), this rule cannot determine authorization intent from attributes,
/// so it is skipped.
/// </para>
/// </summary>
public sealed class OperationSecurityRule : IValidationRule
{
    public string Id => "operation.security";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Error;

    public IEnumerable<ValidationViolation> Validate(OpenApiDocument document, ValidationContext context)
    {
        // Without CLR bindings we cannot determine whether [Authorize] was present.
        if (context.ActionByOperationKey == null) yield break;
        if (document.Paths == null) yield break;

        bool hasGlobalSecurity = document.Security is { Count: > 0 };
        var resolver = new ViolationLocationResolver(context);

        foreach (var (path, pathItem) in document.Paths.OrderBy(kv => kv.Key))
        {
            if (pathItem is not OpenApiPathItem item || item.Operations == null) continue;

            foreach (var (method, operation) in item.Operations.OrderBy(kv => kv.Key.ToString()))
            {
                var key = $"{method.ToString().ToUpperInvariant()} {path}";

                if (!context.ActionByOperationKey.TryGetValue(key, out var info))
                    continue;

                // Check if [Authorize] is present on action or controller
                bool isAuthorized = IsAuthorized(info.Action, info.Controller);
                bool isAnonymous = IsAnonymous(info.Action, info.Controller);

                if (!isAuthorized || isAnonymous) continue;
                if (hasGlobalSecurity) continue;

                // Operation has [Authorize], no [AllowAnonymous], no global security —
                // must declare its own security
                bool hasOperationSecurity = operation.Security is { Count: > 0 };
                if (!hasOperationSecurity)
                {
                    yield return new ValidationViolation(
                        Id,
                        DefaultSeverity,
                        JsonPointerHelper.ForOperation(path, method.ToString()),
                        resolver.ForOperation(key),
                        "Operation is protected by [Authorize] but has no security requirement declared and no global security is defined.");
                }
            }
        }
    }

    private static bool IsAuthorized(
        DotNetOpenApiExtract.Core.Discovery.ActionInfo action,
        DotNetOpenApiExtract.Core.Discovery.ControllerInfo controller)
    {
        const string AuthorizeName = "Microsoft.AspNetCore.Authorization.AuthorizeAttribute";

        return action.Method.GetCustomAttributesData()
                   .Any(a => a.AttributeType.FullName == AuthorizeName)
               || controller.Type.GetCustomAttributesData()
                   .Any(a => a.AttributeType.FullName == AuthorizeName);
    }

    private static bool IsAnonymous(
        DotNetOpenApiExtract.Core.Discovery.ActionInfo action,
        DotNetOpenApiExtract.Core.Discovery.ControllerInfo controller)
    {
        const string AllowAnonName = "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute";

        return action.Method.GetCustomAttributesData()
                   .Any(a => a.AttributeType.FullName == AllowAnonName)
               || controller.Type.GetCustomAttributesData()
                   .Any(a => a.AttributeType.FullName == AllowAnonName);
    }
}
