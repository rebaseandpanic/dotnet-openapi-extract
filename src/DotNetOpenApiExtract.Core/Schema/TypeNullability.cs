using System.Reflection;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// The nullable reference type annotations (<c>?</c>) of one position of a member's declared type: the
/// CLR type at that position paired with the NRT byte the compiler wrote for it, and the same for every
/// position below it.
/// </summary>
/// <remarks>
/// <para>
/// The compiler writes one byte per type of the <b>declared</b> signature, in pre-order: a reference
/// type, an array, a type parameter and a generic value type have a byte of their own followed by their
/// element or type arguments; <c>Nullable&lt;T&gt;</c> is represented by its argument only and a
/// non-generic value type by nothing. 0 = oblivious, 1 = not annotated, 2 = annotated. A single byte
/// stands for every position; without the attribute the nearest <c>NullableContextAttribute</c> (the
/// method, the declaring type, then the enclosing types) gives the value; without either there is no
/// data, which reads as 0.
/// </para>
/// <para>
/// The layout follows the declared, open signature (a member of a generic type is found in its
/// definition by <see cref="MemberInfo.MetadataToken"/>): a type parameter takes one byte whatever is
/// substituted for it, so reading the closed type would shift the positions after it. Below a type
/// parameter the place has no annotations of its own; such positions are marked
/// <see cref="BelowTypeParameter"/>.
/// </para>
/// </remarks>
internal sealed class TypeNullability
{
    private const string NullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttribute = "System.Runtime.CompilerServices.NullableContextAttribute";
    private const string NullableDefinition = "System.Nullable`1";
    private const byte Annotated = 2;

    private readonly IReadOnlyList<TypeNullability> _children;

    private TypeNullability(Type type, byte flag, bool isTypeParameter, bool belowTypeParameter, IReadOnlyList<TypeNullability> children)
    {
        Type = type;
        Flag = flag;
        IsTypeParameter = isTypeParameter;
        BelowTypeParameter = belowTypeParameter;
        _children = children;
    }

    /// <summary>The CLR type at this position, as the member's actual (closed) type has it.</summary>
    public Type Type { get; }

    /// <summary>The NRT byte of this position: 0 oblivious or no data, 1 not annotated, 2 annotated.</summary>
    public byte Flag { get; }

    /// <summary>Whether the declared signature has a type parameter at this position.</summary>
    public bool IsTypeParameter { get; }

    /// <summary>Whether this position lies inside the type substituted for a type parameter of the declaration.</summary>
    public bool BelowTypeParameter { get; }

    /// <summary>
    /// P-NRT2: the position is annotated nullable (byte 2), its CLR type is not a value type (a value
    /// type's <c>Nullable&lt;T&gt;</c> is a type of its own), and it is not below a type parameter of the
    /// declaration (the place does not annotate inside a substituted type).
    /// </summary>
    public bool IsNullable => Flag == Annotated && !Type.IsValueType && !BelowTypeParameter;

    /// <summary>The positions of a property's declared type.</summary>
    public static TypeNullability ForProperty(PropertyInfo property)
    {
        var declared = OpenProperty(property);
        return Build(declared.PropertyType, property.PropertyType,
            Source(AttributeHelper.GetAttribute(declared, NullableAttribute), ContextChain(null, declared.DeclaringType)));
    }

    /// <summary>The positions of a method parameter's declared type.</summary>
    public static TypeNullability ForParameter(ParameterInfo parameter)
    {
        var method = (MethodBase)parameter.Member;
        var declaredMethod = OpenMethod(method);
        var declared = parameter.Position < 0
            ? ((MethodInfo)declaredMethod).ReturnParameter
            : declaredMethod.GetParameters()[parameter.Position];
        return Build(declared.ParameterType, parameter.ParameterType,
            Source(AttributeHelper.GetAttribute(declared, NullableAttribute), ContextChain(declaredMethod, declaredMethod.DeclaringType)));
    }

    /// <summary>The positions of a method's declared return type.</summary>
    public static TypeNullability ForReturn(MethodInfo method) => ForParameter(method.ReturnParameter);

    /// <summary>The position of type argument <paramref name="index"/> of a generic type (<c>Nullable&lt;T&gt;</c> included).</summary>
    public TypeNullability Argument(int index)
    {
        if (!Type.IsGenericType)
            throw new InvalidOperationException($"{Type} has no type arguments.");
        return _children[index];
    }

    /// <summary>The position of the element of an array.</summary>
    public TypeNullability Element()
    {
        if (!Type.IsArray)
            throw new InvalidOperationException($"{Type} is not an array.");
        return _children[0];
    }

    /// <summary>This position with <c>Nullable&lt;T&gt;</c> seen through: the position of <c>T</c>, or this one.</summary>
    public TypeNullability WithoutNullable() =>
        Type.IsGenericType && Type.GetGenericTypeDefinition().FullName == NullableDefinition ? _children[0] : this;

    /// <summary>
    /// The position typed as <paramref name="implemented"/>, an interface the type at this position is or
    /// implements: its type arguments are the positions of the type's own arguments the definition passes
    /// to the interface (<c>Swapped&lt;TValue, TKey&gt; : Dictionary&lt;TKey, TValue&gt;</c> passes its
    /// second argument as the key). An argument the definition fixes to a type of its own has no
    /// annotations of the place.
    /// </summary>
    public TypeNullability Implemented(Type implemented)
    {
        if (SameType(Type, implemented))
            return this;

        var definition = Type.IsGenericType ? Type.GetGenericTypeDefinition() : Type;
        var arguments = Type.IsGenericType ? Type.GetGenericArguments() : [];
        var declared = definition.GetInterfaces().FirstOrDefault(i => Matches(i, implemented, arguments))
            ?? throw new InvalidOperationException($"{Type} does not implement {implemented}.");

        return Substitute(declared, implemented, Flag);
    }

    /// <summary>The position of <paramref name="declared"/> (written with the type parameters of this position's definition) closed as <paramref name="actual"/>.</summary>
    private TypeNullability Substitute(Type declared, Type actual, byte flag)
    {
        if (declared.IsGenericParameter && declared.DeclaringMethod is null)
            return _children[declared.GenericParameterPosition];

        if (declared.IsArray)
            return new TypeNullability(actual, flag, false, BelowTypeParameter,
                [Substitute(declared.GetElementType()!, actual.GetElementType()!, 0)]);

        if (declared.IsGenericType)
        {
            var declaredArguments = declared.GetGenericArguments();
            var actualArguments = actual.GetGenericArguments();
            return new TypeNullability(actual, flag, false, BelowTypeParameter,
                declaredArguments.Select((a, i) => Substitute(a, actualArguments[i], 0)).ToList());
        }

        return new TypeNullability(actual, flag, false, BelowTypeParameter, []);
    }

    /// <summary>Whether <paramref name="declared"/>, with the type parameters replaced by <paramref name="arguments"/>, is <paramref name="actual"/>.</summary>
    private static bool Matches(Type declared, Type actual, Type[] arguments)
    {
        if (declared.IsGenericParameter && declared.DeclaringMethod is null)
            return SameType(arguments[declared.GenericParameterPosition], actual);

        if (declared.IsArray)
            return actual.IsArray
                && declared.GetArrayRank() == actual.GetArrayRank()
                && Matches(declared.GetElementType()!, actual.GetElementType()!, arguments);

        if (declared.IsGenericType)
        {
            if (!actual.IsGenericType
                || declared.GetGenericTypeDefinition().FullName != actual.GetGenericTypeDefinition().FullName)
                return false;
            var declaredArguments = declared.GetGenericArguments();
            var actualArguments = actual.GetGenericArguments();
            return declaredArguments.Length == actualArguments.Length
                && declaredArguments.Select((a, i) => Matches(a, actualArguments[i], arguments)).All(m => m);
        }

        return SameType(declared, actual);
    }

    private static bool SameType(Type left, Type right) =>
        ReferenceEquals(left, right)
        || (left.FullName is { } name && string.Equals(name, right.FullName, StringComparison.Ordinal));

    // -------------------------------------------------------------------------
    // Building the tree
    // -------------------------------------------------------------------------

    /// <summary>The bytes of one declared type: per position, or one value for every position.</summary>
    private sealed record ByteSource(IReadOnlyList<byte>? PerPosition, byte Uniform)
    {
        public byte At(int position) =>
            PerPosition is null ? Uniform : position < PerPosition.Count ? PerPosition[position] : (byte)0;
    }

    private static TypeNullability Build(Type declared, Type actual, ByteSource bytes)
    {
        var position = 0;
        return Build(declared, actual, bytes, ref position);
    }

    private static TypeNullability Build(Type declared, Type actual, ByteSource bytes, ref int position)
    {
        if (declared.IsGenericParameter)
        {
            var flag = bytes.At(position++);
            // A single byte reads the same at every position, the substituted type's included; it is
            // still not an annotation of the place there.
            return new TypeNullability(actual, flag, true, false, Below(actual, bytes.PerPosition is null ? bytes.Uniform : (byte)0));
        }

        var own = HasOwnByte(declared) ? bytes.At(position++) : (byte)0;

        if (declared.IsArray)
        {
            var element = Build(declared.GetElementType()!, actual.GetElementType()!, bytes, ref position);
            return new TypeNullability(actual, own, false, false, [element]);
        }

        if (declared.IsGenericType)
        {
            var declaredArguments = declared.GetGenericArguments();
            var actualArguments = actual.GetGenericArguments();
            var children = new List<TypeNullability>(declaredArguments.Length);
            for (var i = 0; i < declaredArguments.Length; i++)
                children.Add(Build(declaredArguments[i], actualArguments[i], bytes, ref position));
            return new TypeNullability(actual, own, false, false, children);
        }

        return new TypeNullability(actual, own, false, false, []);
    }

    /// <summary>The positions of a type substituted for a type parameter: no annotations of the place.</summary>
    private static IReadOnlyList<TypeNullability> Below(Type actual, byte flag)
    {
        if (actual.IsArray)
            return [new TypeNullability(actual.GetElementType()!, flag, false, true, Below(actual.GetElementType()!, flag))];

        if (actual.IsGenericType)
            return actual.GetGenericArguments().Select(a => new TypeNullability(a, flag, false, true, Below(a, flag))).ToList();

        return [];
    }

    /// <summary>
    /// Whether a declared type takes a byte of its own: a reference type, an array, a type parameter and
    /// a generic value type do; a non-generic value type and <c>Nullable&lt;T&gt;</c> (only its argument
    /// counts) do not.
    /// </summary>
    private static bool HasOwnByte(Type type)
    {
        if (!type.IsValueType || type.IsGenericParameter)
            return true;

        return type.IsGenericType && type.GetGenericTypeDefinition().FullName != NullableDefinition;
    }

    private static ByteSource Source(CustomAttributeData? nullable, IEnumerable<MemberInfo> contextChain)
    {
        if (nullable is { ConstructorArguments: [var argument] })
        {
            switch (argument.Value)
            {
                case byte single:
                    return new ByteSource(null, single);
                case IReadOnlyCollection<CustomAttributeTypedArgument> values:
                    return new ByteSource(values.Select(v => v.Value is byte b ? b : (byte)0).ToList(), 0);
            }
        }

        foreach (var member in contextChain)
        {
            if (AttributeHelper.GetAttribute(member, NullableContextAttribute) is { ConstructorArguments: [{ Value: byte context }] })
                return new ByteSource(null, context);
        }

        return new ByteSource(null, 0);
    }

    /// <summary>The members whose <c>NullableContextAttribute</c> applies, nearest first: the method, the declaring type, the enclosing types.</summary>
    private static IEnumerable<MemberInfo> ContextChain(MethodBase? method, Type? declaringType)
    {
        if (method is not null)
            yield return method;
        for (var type = declaringType; type is not null; type = type.DeclaringType)
            yield return type;
    }

    /// <summary>The property as its declaring type's definition declares it.</summary>
    private static PropertyInfo OpenProperty(PropertyInfo property)
    {
        var declaringType = property.DeclaringType;
        if (declaringType is not { IsGenericType: true, IsGenericTypeDefinition: false })
            return property;

        return declaringType.GetGenericTypeDefinition()
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .First(p => p.MetadataToken == property.MetadataToken && p.Module.Equals(property.Module));
    }

    /// <summary>The method as its declaring type's definition (and its own generic definition) declares it.</summary>
    private static MethodBase OpenMethod(MethodBase method)
    {
        if (method is MethodInfo { IsGenericMethod: true, IsGenericMethodDefinition: false } constructed)
            method = constructed.GetGenericMethodDefinition();

        var declaringType = method.DeclaringType;
        if (declaringType is not { IsGenericType: true, IsGenericTypeDefinition: false })
            return method;

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var definition = declaringType.GetGenericTypeDefinition();
        IEnumerable<MethodBase> candidates = method is ConstructorInfo
            ? definition.GetConstructors(All)
            : definition.GetMethods(All);
        return candidates.First(m => m.MetadataToken == method.MetadataToken && m.Module.Equals(method.Module));
    }
}
