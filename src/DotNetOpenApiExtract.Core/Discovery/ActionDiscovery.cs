using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Discovery;

/// <summary>
/// Represents a single discovered API action (endpoint) within a controller.
/// A method with multiple HTTP method attributes produces one <see cref="ActionInfo"/> per attribute.
/// </summary>
public sealed class ActionInfo
{
    /// <summary>The reflected method that implements the action.</summary>
    public required MethodInfo Method { get; init; }

    /// <summary>
    /// The action name. Taken from [ActionName] if present, otherwise from
    /// <see cref="MethodInfo.Name"/>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The HTTP method string: GET, POST, PUT, DELETE, PATCH, HEAD, or OPTIONS.</summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    /// The route template from the HTTP method attribute constructor argument, or
    /// <see langword="null"/> if none was specified.
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>The controller that owns this action.</summary>
    public required ControllerInfo Controller { get; init; }
}

/// <summary>
/// Discovers API actions (endpoints) within controllers using reflection-only metadata.
/// All attribute inspection is performed through <see cref="AttributeHelper"/> so that
/// assemblies loaded via <c>MetadataLoadContext</c> are supported.
/// </summary>
public static class ActionDiscovery
{
    /// <summary>
    /// Mapping from HTTP attribute full name to the canonical HTTP method string.
    /// Values are already uppercase — no ToUpperInvariant() needed at usage sites.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> HttpAttributeMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AttributeHelper.Names.HttpGet]     = "GET",
            [AttributeHelper.Names.HttpPost]    = "POST",
            [AttributeHelper.Names.HttpPut]     = "PUT",
            [AttributeHelper.Names.HttpDelete]  = "DELETE",
            [AttributeHelper.Names.HttpPatch]   = "PATCH",
            [AttributeHelper.Names.HttpHead]    = "HEAD",
            [AttributeHelper.Names.HttpOptions] = "OPTIONS",
        };

    /// <summary>
    /// Discovers all actions declared directly on the given <paramref name="controller"/>.
    /// </summary>
    /// <param name="controller">The controller to inspect.</param>
    /// <returns>
    /// A list of <see cref="ActionInfo"/> instances. A method with multiple HTTP method
    /// attributes contributes one entry per attribute.
    /// </returns>
    public static IReadOnlyList<ActionInfo> DiscoverActions(ControllerInfo controller)
    {
        var result = new List<ActionInfo>();

        // BindingFlags.DeclaredOnly ensures we do not pick up inherited ControllerBase methods.
        MethodInfo[] methods;
        try
        {
            methods = controller.Type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            return result;
        }

        foreach (var method in methods)
        {
            // Exclusion: [NonAction]
            if (AttributeHelper.HasAttribute(method, AttributeHelper.Names.NonAction))
                continue;

            // Exclusion: [ApiExplorerSettings(IgnoreApi = true)]
            var explorerSettings = AttributeHelper.GetAttribute(method, AttributeHelper.Names.ApiExplorerSettings);
            if (explorerSettings != null)
            {
                var ignoreApi = AttributeHelper.GetNamedArgument<bool>(explorerSettings, "IgnoreApi");
                if (ignoreApi)
                    continue;
            }

            // Determine the action name: [ActionName] overrides the method name.
            var actionNameAttr = AttributeHelper.GetAttribute(method, AttributeHelper.Names.ActionName);
            var actionName = actionNameAttr != null
                ? AttributeHelper.GetConstructorArgument<string>(actionNameAttr, 0) ?? method.Name
                : method.Name;

            // Collect HTTP method attributes — skip method entirely if none found.
            // Enumerate directly without ToList() to avoid per-method heap allocation.
            bool foundAny = false;
            foreach (var attrData in method.GetCustomAttributesData())
            {
                var fullName = attrData.AttributeType.FullName;
                if (fullName == null || !HttpAttributeMap.TryGetValue(fullName, out var httpMethod))
                    continue;

                foundAny = true;

                // Route template: optional first constructor argument of the HTTP attribute.
                var routeTemplate = AttributeHelper.GetConstructorArgument<string>(attrData, 0);
                if (string.IsNullOrEmpty(routeTemplate))
                    routeTemplate = null;

                result.Add(new ActionInfo
                {
                    Method = method,
                    Name = actionName,
                    HttpMethod = httpMethod,
                    RouteTemplate = routeTemplate,
                    Controller = controller,
                });
            }

            // foundAny is set but not used for early-continue here because the adds
            // already happened inside the loop. The variable avoids the need for a
            // separate "has any HTTP attr?" pre-pass.
            _ = foundAny;
        }

        return result;
    }

    /// <summary>
    /// Discovers all actions across a collection of controllers.
    /// </summary>
    /// <param name="controllers">The controllers to inspect.</param>
    /// <returns>A flat list of all discovered <see cref="ActionInfo"/> instances.</returns>
    public static IReadOnlyList<ActionInfo> DiscoverActions(IEnumerable<ControllerInfo> controllers)
    {
        return controllers.SelectMany(DiscoverActions).ToList();
    }
}
