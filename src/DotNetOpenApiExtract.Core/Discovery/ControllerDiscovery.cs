using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Discovery;

/// <summary>
/// Represents a discovered API controller in a loaded assembly.
/// </summary>
public sealed class ControllerInfo
{
    /// <summary>The reflected controller type.</summary>
    public required Type Type { get; init; }

    /// <summary>The controller name with the "Controller" suffix removed.</summary>
    public required string Name { get; init; }

    /// <summary>The route template from the [Route] attribute, if present.</summary>
    public string? RouteTemplate { get; init; }

    /// <summary>The tag description from the [SwaggerTag] attribute, if present.</summary>
    public string? TagDescription { get; init; }

    /// <summary>The API explorer group name from [ApiExplorerSettings(GroupName = "...")], if present.</summary>
    public string? GroupName { get; init; }
}

/// <summary>
/// Discovers API controllers in a loaded <see cref="Assembly"/> using reflection-only metadata.
/// All attribute inspection is performed through <see cref="AttributeHelper"/> so that the
/// assembly can be loaded via <c>MetadataLoadContext</c> without executing any code.
/// </summary>
public static class ControllerDiscovery
{
    private const string ControllerBaseFull = "Microsoft.AspNetCore.Mvc.ControllerBase";
    private const string ControllerFull = "Microsoft.AspNetCore.Mvc.Controller";
    private const string ControllerSuffix = "Controller";

    /// <summary>
    /// Returns all controller types found in <paramref name="assembly"/>.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>A list of <see cref="ControllerInfo"/> instances, one per discovered controller.</returns>
    public static IReadOnlyList<ControllerInfo> DiscoverControllers(Assembly assembly)
    {
        var result = new List<ControllerInfo>();

        Type[] allTypes;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray()!;
        }

        foreach (var type in allTypes)
        {
            try
            {
                // Fetch attribute data once per type; pass to both check and info-building.
                var typeAttrs = type.GetCustomAttributesData();
                if (!IsController(type, typeAttrs))
                    continue;

                result.Add(BuildControllerInfo(type, typeAttrs));
            }
            catch (Exception ex) when (ex is FileNotFoundException
                                        or FileLoadException
                                        or BadImageFormatException
                                        or TypeLoadException)
            {
                // Type has unresolvable dependencies — skip it silently.
                // Common when the DLL is provided without all its NuGet references.
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static bool IsController(Type type, IList<CustomAttributeData> typeAttrs)
    {
        // Must be a concrete, public class
        if (!type.IsClass || type.IsAbstract || !type.IsPublic)
            return false;

        // Must have [ApiController] attribute OR inherit from ControllerBase/Controller
        bool isControllerByConvention =
            AttributeHelper.HasAttribute(typeAttrs, AttributeHelper.Names.ApiController)
            || InheritsFromControllerBase(type);

        if (!isControllerByConvention)
            return false;

        // Exclusion: [NonController]
        if (AttributeHelper.HasAttribute(typeAttrs, AttributeHelper.Names.NonController))
            return false;

        // Exclusion: [ApiExplorerSettings(IgnoreApi = true)]
        var explorerSettings = AttributeHelper.GetAttribute(typeAttrs, AttributeHelper.Names.ApiExplorerSettings);
        if (explorerSettings != null)
        {
            var ignoreApi = AttributeHelper.GetNamedArgument<bool>(explorerSettings, "IgnoreApi");
            if (ignoreApi)
                return false;
        }

        // Exclusion: [ExcludeFromDescription]
        if (AttributeHelper.HasAttribute(typeAttrs, AttributeHelper.Names.ExcludeFromDescription))
            return false;

        return true;
    }

    /// <summary>
    /// Walks the base type chain to check for ControllerBase or Controller inheritance.
    /// Intentionally includes both ControllerBase (API) and Controller (MVC views) to match
    /// ASP.NET Core routing conventions.
    /// </summary>
    private static bool InheritsFromControllerBase(Type type)
    {
        var current = type.BaseType;
        while (current != null)
        {
            var fullName = current.FullName;
            if (fullName == ControllerBaseFull || fullName == ControllerFull)
                return true;

            current = current.BaseType;
        }

        return false;
    }

    private static ControllerInfo BuildControllerInfo(Type type, IList<CustomAttributeData> typeAttrs)
    {
        // Name: strip trailing "Controller" suffix if present
        var name = type.Name.EndsWith(ControllerSuffix, StringComparison.Ordinal)
            ? type.Name[..^ControllerSuffix.Length]
            : type.Name;

        // RouteTemplate: first constructor argument of [Route]
        string? routeTemplate = null;
        var routeAttr = AttributeHelper.GetAttribute(typeAttrs, AttributeHelper.Names.Route);
        if (routeAttr != null)
            routeTemplate = AttributeHelper.GetConstructorArgument<string>(routeAttr, 0);

        // TagDescription: first constructor argument of [SwaggerTag]
        string? tagDescription = null;
        var swaggerTagAttr = AttributeHelper.GetAttribute(typeAttrs, AttributeHelper.Names.SwaggerTag);
        if (swaggerTagAttr != null)
            tagDescription = AttributeHelper.GetConstructorArgument<string>(swaggerTagAttr, 0);

        // GroupName: named argument "GroupName" of [ApiExplorerSettings] (already fetched in IsController)
        string? groupName = null;
        var explorerAttr = AttributeHelper.GetAttribute(typeAttrs, AttributeHelper.Names.ApiExplorerSettings);
        if (explorerAttr != null)
            groupName = AttributeHelper.GetNamedArgument<string>(explorerAttr, "GroupName");

        return new ControllerInfo
        {
            Type = type,
            Name = name,
            RouteTemplate = routeTemplate,
            TagDescription = tagDescription,
            GroupName = groupName,
        };
    }
}
