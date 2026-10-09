using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Reads the nullable annotation (<c>?</c>) of the element of an asynchronous sequence returned by an
/// action, from the compiler's <c>NullableAttribute</c> / <c>NullableContextAttribute</c> metadata.
/// </summary>
/// <remarks>
/// The compiler writes one byte per type in the return type tree, in pre-order: a reference type,
/// an array, a type parameter and a generic value type have a byte of its own followed by their
/// element or type arguments; <c>Nullable&lt;T&gt;</c> is represented by its argument only and a
/// non-generic value type by nothing. 0 = oblivious, 1 = not annotated,
/// 2 = annotated. A single byte stands for every position; without the attribute the nearest
/// <c>NullableContextAttribute</c> (method, then enclosing types) gives the value.
/// </remarks>
internal static class ReturnNullability
{
    private const string NullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttribute = "System.Runtime.CompilerServices.NullableContextAttribute";
    private const byte Annotated = 2;

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
        var type = method.ReturnType;
        var position = 0;

        while (type.IsGenericType && Wrappers.Contains(type.GetGenericTypeDefinition().FullName ?? string.Empty))
        {
            position = ArgumentStart(type, position, 0);
            type = type.GetGenericArguments()[0];
        }

        if (!type.IsGenericType || type.GetGenericTypeDefinition().FullName != AsyncEnumerable)
            return false;

        var element = type.GetGenericArguments()[0];
        if (element.IsValueType)
            return false;

        return FlagAt(method, ArgumentStart(type, position, 0)) == Annotated;
    }

    /// <summary>
    /// Whether a type takes a byte of its own: a reference type, an array, a type parameter and a
    /// generic value type do; a non-generic value type and <c>Nullable&lt;T&gt;</c> (only its argument
    /// counts) do not.
    /// </summary>
    private static bool HasOwnByte(Type type)
    {
        if (!type.IsValueType || type.IsGenericParameter)
            return true;

        return type.IsGenericType && type.GetGenericTypeDefinition().FullName != "System.Nullable`1";
    }

    /// <summary>Number of bytes the subtree of <paramref name="type"/> takes.</summary>
    private static int Width(Type type)
    {
        var width = HasOwnByte(type) ? 1 : 0;
        if (type.IsArray)
            return width + Width(type.GetElementType()!);
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                width += Width(argument);
        }
        return width;
    }

    /// <summary>Position of the type argument <paramref name="index"/> of <paramref name="type"/> at <paramref name="position"/>.</summary>
    private static int ArgumentStart(Type type, int position, int index)
    {
        var start = position + (HasOwnByte(type) ? 1 : 0);
        var arguments = type.GetGenericArguments();
        for (var i = 0; i < index; i++)
            start += Width(arguments[i]);
        return start;
    }

    private static byte FlagAt(MethodInfo method, int position)
    {
        var attribute = AttributeHelper.GetAttribute(method.ReturnParameter, NullableAttribute);
        if (attribute is { ConstructorArguments: [var argument] })
        {
            switch (argument.Value)
            {
                case byte single:
                    return single;
                case IReadOnlyCollection<CustomAttributeTypedArgument> bytes:
                    var list = bytes.Select(b => b.Value is byte value ? value : (byte)0).ToList();
                    return position < list.Count ? list[position] : (byte)0;
            }
        }

        return Context(method) ?? 0;
    }

    private static byte? Context(MethodInfo method)
    {
        if (ContextOf(method) is { } own)
            return own;

        for (var type = method.DeclaringType; type != null; type = type.DeclaringType)
        {
            if (ContextOf(type) is { } inherited)
                return inherited;
        }

        return null;
    }

    private static byte? ContextOf(MemberInfo member) =>
        AttributeHelper.GetAttribute(member, NullableContextAttribute) is { ConstructorArguments: [{ Value: byte value }] }
            ? value
            : null;
}
