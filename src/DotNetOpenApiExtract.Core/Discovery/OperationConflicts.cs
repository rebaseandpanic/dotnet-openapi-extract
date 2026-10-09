namespace DotNetOpenApiExtract.Core.Discovery;

/// <summary>
/// The actions that declare one path and HTTP method. <see cref="Winner"/> is the action the
/// document uses; <see cref="Others"/> lost the conflict (empty when there is none).
/// </summary>
internal sealed record OperationGroup(string Path, string HttpMethod, ActionInfo Winner, IReadOnlyList<ActionInfo> Others)
{
    /// <summary>All actions of the group, winner first, then the others by the same key.</summary>
    public IEnumerable<ActionInfo> All => Others.Prepend(Winner);
}

/// <summary>
/// Picks one action per path and HTTP method, independently of discovery order. The key compares,
/// ordinally: the controller type's full name, the action method's name, the full names of its
/// parameter types in order (overloads), and the producing attribute's position in metadata. The
/// smallest key wins. The same choice is used for the document and for validation bindings.
/// </summary>
internal static class OperationConflicts
{
    /// <summary>The winner key; the smaller action wins.</summary>
    public static IComparer<ActionInfo> Key { get; } = Comparer<ActionInfo>.Create(Compare);

    /// <summary>
    /// Groups <paramref name="actions"/> by path and method (methods compared case-insensitively,
    /// like <see cref="System.Net.Http.HttpMethod"/>). Groups keep the order in which their first
    /// action was discovered, so documents without conflicts keep their path order.
    /// </summary>
    public static IReadOnlyList<OperationGroup> Group(IEnumerable<ActionInfo> actions)
    {
        var groups = new List<(string Path, string Method, List<ActionInfo> Actions)>();
        var index = new Dictionary<(string, string), int>();

        foreach (var action in actions)
        {
            var path = RouteBuilder.BuildPath(
                action.Controller.RouteTemplate,
                action.RouteTemplate,
                action.Controller.Type.Name,
                action.Name);
            var key = (path, action.HttpMethod.ToUpperInvariant());

            if (!index.TryGetValue(key, out var position))
            {
                position = groups.Count;
                index[key] = position;
                groups.Add((path, action.HttpMethod, []));
            }

            groups[position].Actions.Add(action);
        }

        return groups
            .Select(g =>
            {
                var ordered = g.Actions.OrderBy(a => a, Key).ToList();
                return new OperationGroup(g.Path, ordered[0].HttpMethod, ordered[0], ordered.Skip(1).ToList());
            })
            .ToList();
    }

    /// <summary>The action as named in warnings: controller type's full name and method name.</summary>
    public static string DisplayName(ActionInfo action) =>
        $"{action.Controller.Type.FullName ?? action.Controller.Type.Name}.{action.Method.Name}";

    private static int Compare(ActionInfo? x, ActionInfo? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var result = string.CompareOrdinal(
            x.Controller.Type.FullName ?? x.Controller.Type.Name,
            y.Controller.Type.FullName ?? y.Controller.Type.Name);
        if (result != 0) return result;

        result = string.CompareOrdinal(x.Method.Name, y.Method.Name);
        if (result != 0) return result;

        var xParameters = ParameterTypeNames(x);
        var yParameters = ParameterTypeNames(y);
        for (var i = 0; i < Math.Min(xParameters.Count, yParameters.Count); i++)
        {
            result = string.CompareOrdinal(xParameters[i], yParameters[i]);
            if (result != 0) return result;
        }

        result = xParameters.Count.CompareTo(yParameters.Count);
        if (result != 0) return result;

        return x.AttributeOrder.CompareTo(y.AttributeOrder);
    }

    private static IReadOnlyList<string> ParameterTypeNames(ActionInfo action) =>
        action.Method.GetParameters()
            .Select(p => p.ParameterType.FullName ?? p.ParameterType.Name)
            .ToList();
}
