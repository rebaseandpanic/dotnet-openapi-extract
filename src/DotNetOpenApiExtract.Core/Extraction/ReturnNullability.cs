using System.Reflection;
using DotNetOpenApiExtract.Core.Schema;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Reads the nullable annotation (<c>?</c>) of the element of an asynchronous sequence returned by an
/// action, from the positions of its declared return type (<see cref="TypeNullability"/>).
/// </summary>
internal static class ReturnNullability
{
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask`1",
        "Microsoft.AspNetCore.Mvc.ActionResult`1",
    };

    private const string AsyncEnumerable = "System.Collections.Generic.IAsyncEnumerable`1";

    /// <summary>
    /// Whether the return type of <paramref name="method"/> is, through <c>Task&lt;&gt;</c>,
    /// <c>ValueTask&lt;&gt;</c> and <c>ActionResult&lt;&gt;</c>, an <c>IAsyncEnumerable&lt;T?&gt;</c>
    /// whose reference-type element is annotated nullable. <see langword="false"/> for an oblivious
    /// (<c>#nullable disable</c>) or non-annotated element, and for value types (whose
    /// <c>Nullable&lt;T&gt;</c> the schema already shows).
    /// </summary>
    public static bool AsyncSequenceElementIsNullable(MethodInfo method)
    {
        var position = TypeNullability.ForReturn(method);

        while (position.Type.IsGenericType && Wrappers.Contains(position.Type.GetGenericTypeDefinition().FullName ?? string.Empty))
            position = position.Argument(0);

        return position.Type.IsGenericType
            && position.Type.GetGenericTypeDefinition().FullName == AsyncEnumerable
            && position.Argument(0).IsNullable;
    }
}
