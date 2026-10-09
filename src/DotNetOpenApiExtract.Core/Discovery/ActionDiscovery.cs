using System.Reflection;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Discovery;

/// <summary>
/// Represents a single discovered API action (endpoint) within a controller: one HTTP method on
/// one route. A method with several HTTP methods (several attributes, or one
/// <c>[AcceptVerbs]</c> with several methods) produces one <see cref="ActionInfo"/> per method and route.
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

    /// <summary>
    /// The HTTP method. Standard methods (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, TRACE,
    /// CONNECT, QUERY) are upper case; any other method from <c>[AcceptVerbs]</c> keeps the
    /// capitalization of the attribute literal, which is what goes into the request.
    /// </summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    /// The route template from the HTTP method attribute (constructor argument of <c>[Http*]</c>,
    /// named <c>Route</c> of <c>[AcceptVerbs]</c>), or <see langword="null"/> if none was specified.
    /// </summary>
    public string? RouteTemplate { get; init; }

    /// <summary>The controller that owns this action.</summary>
    public required ControllerInfo Controller { get; init; }

    /// <summary>
    /// The <c>Name</c> of the attribute that produced this operation (<c>[HttpGet(Name = …)]</c>,
    /// <c>[AcceptVerbs(…, Name = …)]</c>), or <see langword="null"/>.
    /// </summary>
    public string? OperationName { get; init; }

    /// <summary>
    /// Position of the producing attribute in the method's attribute metadata
    /// (<see cref="MemberInfo.GetCustomAttributesData"/>); a deterministic tie-breaker.
    /// </summary>
    public int AttributeOrder { get; init; }
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
    /// Warnings are printed to <c>Console.Error</c>.
    /// </summary>
    /// <param name="controller">The controller to inspect.</param>
    /// <returns>One <see cref="ActionInfo"/> per HTTP method and route of each action.</returns>
    public static IReadOnlyList<ActionInfo> DiscoverActions(ControllerInfo controller)
        => DiscoverActions(controller, onDiagnostic: null);

    /// <summary>
    /// Discovers all actions declared directly on the given <paramref name="controller"/>.
    /// </summary>
    /// <param name="controller">The controller to inspect.</param>
    /// <param name="onDiagnostic">
    /// Receives warnings about actions that are skipped (an empty <c>[AcceptVerbs()]</c>, an HTTP
    /// method attribute whose methods are not statically known). When <see langword="null"/>,
    /// warnings are printed to <c>Console.Error</c>.
    /// </param>
    /// <returns>One <see cref="ActionInfo"/> per HTTP method and route of each action.</returns>
    public static IReadOnlyList<ActionInfo> DiscoverActions(
        ControllerInfo controller, Action<ExtractionDiagnostic>? onDiagnostic)
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

            var found = CollectHttpMethods(controller, method, onDiagnostic);
            if (found == null)
                continue; // skipped with a warning

            foreach (var (httpMethod, routeTemplate, operationName, order) in found)
            {
                result.Add(new ActionInfo
                {
                    Method         = method,
                    Name           = actionName,
                    HttpMethod     = httpMethod,
                    RouteTemplate  = routeTemplate,
                    Controller     = controller,
                    OperationName  = operationName,
                    AttributeOrder = order,
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Discovers all actions across a collection of controllers. Warnings are printed to <c>Console.Error</c>.
    /// </summary>
    /// <param name="controllers">The controllers to inspect.</param>
    /// <returns>A flat list of all discovered <see cref="ActionInfo"/> instances.</returns>
    public static IReadOnlyList<ActionInfo> DiscoverActions(IEnumerable<ControllerInfo> controllers)
        => DiscoverActions(controllers, onDiagnostic: null);

    /// <summary>Discovers all actions across a collection of controllers.</summary>
    /// <param name="controllers">The controllers to inspect.</param>
    /// <param name="onDiagnostic">
    /// Receives warnings about skipped actions; when <see langword="null"/>, they are printed to <c>Console.Error</c>.
    /// </param>
    /// <returns>A flat list of all discovered <see cref="ActionInfo"/> instances.</returns>
    public static IReadOnlyList<ActionInfo> DiscoverActions(
        IEnumerable<ControllerInfo> controllers, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        return controllers.SelectMany(c => DiscoverActions(c, onDiagnostic)).ToList();
    }

    /// <summary>
    /// Reads the (method, route) pairs of one action from <c>[Http*]</c> attributes (and their
    /// subclasses) and <c>[AcceptVerbs]</c>, in metadata order, without duplicates. Returns
    /// <see langword="null"/> — after a warning — when the action must not be emitted: an empty
    /// <c>[AcceptVerbs()]</c>, or an attribute derived from <c>HttpMethodAttribute</c> whose methods
    /// are not statically visible (they are never guessed from the attribute's name).
    /// </summary>
    private static List<(string Method, string? Route, string? Name, int Order)>? CollectHttpMethods(
        ControllerInfo controller, MethodInfo method, Action<ExtractionDiagnostic>? onDiagnostic)
    {
        var found = new List<(string Method, string? Route, string? Name, int Order)>();
        var seen = new HashSet<(string, string?)>();

        void Add(string httpMethod, string? route, string? name, int order)
        {
            var normalized = Versioning.OperationPlacement.NormalizeMethod(httpMethod);
            if (seen.Add((normalized.ToUpperInvariant(), route)))
                found.Add((normalized, route, name, order));
        }

        var attributes = method.GetCustomAttributesData();
        for (var order = 0; order < attributes.Count; order++)
        {
            var attrData = attributes[order];
            var attributeType = attrData.AttributeType;
            var fullName = attributeType.FullName;
            if (fullName == null)
                continue;

            var name = AttributeHelper.GetNamedArgument<string>(attrData, "Name");

            if (fullName == AttributeHelper.Names.AcceptVerbs)
            {
                var verbs = ReadAcceptVerbs(attrData);
                if (verbs.Count == 0)
                {
                    DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
                    {
                        Code     = ExtractionDiagnosticCodes.DiscoveryEmptyAcceptVerbs,
                        Message  = $"[AcceptVerbs] without methods on {ActionDisplayName(controller, method)} — action skipped.",
                        Subjects = [ActionDisplayName(controller, method)],
                    });
                    return null;
                }

                var route = NullIfEmpty(AttributeHelper.GetNamedArgument<string>(attrData, "Route"));
                foreach (var verb in verbs)
                    Add(verb, route, name, order);
                continue;
            }

            if (KnownHttpMethod(attributeType) is { } httpMethod)
            {
                Add(httpMethod, NullIfEmpty(AttributeHelper.GetConstructorArgument<string>(attrData, 0)), name, order);
                continue;
            }

            if (DerivesFrom(attributeType, AttributeHelper.Names.HttpMethodAttributeBase))
            {
                DiagnosticBag.Deliver(onDiagnostic, new ExtractionDiagnostic
                {
                    Code     = ExtractionDiagnosticCodes.DiscoveryUnknownHttpMethodAttribute,
                    Message  = $"{fullName} on {ActionDisplayName(controller, method)}: its HTTP methods are not " +
                               "statically known — action skipped; use [AcceptVerbs(\"METHOD\")] instead.",
                    Subjects = [fullName],
                });
                return null;
            }
        }

        return found.Count > 0 ? found : null;
    }

    /// <summary>
    /// The method of a built-in <c>[Http*]</c> attribute or of a subclass of one (the subclass
    /// inherits the built-in's fixed method); <see langword="null"/> for anything else.
    /// </summary>
    private static string? KnownHttpMethod(Type attributeType)
    {
        for (var type = attributeType; type != null; type = SafeBaseType(type))
        {
            if (type.FullName is { } name && HttpAttributeMap.TryGetValue(name, out var httpMethod))
                return httpMethod;
        }

        return null;
    }

    private static bool DerivesFrom(Type type, string baseFullName)
    {
        for (var current = SafeBaseType(type); current != null; current = SafeBaseType(current))
        {
            if (current.FullName == baseFullName)
                return true;
        }

        return false;
    }

    private static Type? SafeBaseType(Type type)
    {
        try
        {
            return type.BaseType;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            return null;
        }
    }

    /// <summary>
    /// Methods of <c>[AcceptVerbs("GET")]</c> (<c>string</c> constructor) or
    /// <c>[AcceptVerbs("GET", "POST")]</c> (<c>params string[]</c>), as literals.
    /// </summary>
    private static IReadOnlyList<string> ReadAcceptVerbs(CustomAttributeData attrData)
    {
        if (attrData.ConstructorArguments.Count == 0)
            return [];

        return attrData.ConstructorArguments[0].Value switch
        {
            string single when !string.IsNullOrWhiteSpace(single) => [single],
            IReadOnlyCollection<CustomAttributeTypedArgument> many => many
                .Select(a => a.Value as string)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList(),
            _ => [],
        };
    }

    private static string ActionDisplayName(ControllerInfo controller, MethodInfo method)
        => $"{controller.Type.FullName}.{method.Name}";

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
