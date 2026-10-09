namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Recognizes asynchronous sequences (<c>IAsyncEnumerable&lt;T&gt;</c>) by metadata name, so the
/// check works for types loaded through <c>MetadataLoadContext</c>.
/// </summary>
internal static class StreamingTypes
{
    private const string AsyncEnumerableFullName = "System.Collections.Generic.IAsyncEnumerable`1";

    /// <summary>
    /// When <paramref name="type"/> is <c>IAsyncEnumerable&lt;T&gt;</c> or implements it, returns
    /// <see langword="true"/> and the element type <c>T</c>.
    /// </summary>
    public static bool TryGetAsyncEnumerableElementType(Type type, out Type elementType)
    {
        if (IsAsyncEnumerable(type))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        foreach (var implemented in type.GetInterfaces())
        {
            if (IsAsyncEnumerable(implemented))
            {
                elementType = implemented.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsAsyncEnumerable(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition().FullName == AsyncEnumerableFullName;
}
