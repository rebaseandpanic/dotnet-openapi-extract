namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>Serialization context a component schema describes.</summary>
internal enum SchemaContext
{
    /// <summary>MVC controller bodies, serialized with the MVC JSON options.</summary>
    Mvc,

    /// <summary>
    /// Typed <c>IResult</c> bodies and server-sent events data, serialized with the HTTP JSON
    /// options; used only when those options differ from the MVC ones in what shapes a schema.
    /// </summary>
    Http,
}

/// <summary>What a component schema stands for, besides its CLR type.</summary>
internal enum SchemaRole
{
    /// <summary>The type itself, as written when it is used directly.</summary>
    Direct,

    /// <summary>A polymorphic base: the union of its alternatives (<c>oneOf</c> / <c>anyOf</c>).</summary>
    Union,

    /// <summary>A derived type in the polymorphic use of one base: its properties plus the discriminator.</summary>
    Variant,

    /// <summary>The base branch of a concrete polymorphic base: the base object itself within its union.</summary>
    BaseDefault,
}

/// <summary>
/// Identity of one component schema: the CLR type, the serialization context and the role (for a
/// variant, also the base it is a variant of).
/// The type is identified by its full name, so closed generic types with different arguments
/// (or different definitions) are different keys.
/// </summary>
internal readonly record struct SchemaKey(string TypeIdentity, SchemaContext Context, SchemaRole Role, string? RoleBase = null)
{
    public static SchemaKey Direct(Type type, SchemaContext context = SchemaContext.Mvc) =>
        new(Identity(type), context, SchemaRole.Direct);

    public static SchemaKey Union(Type baseType, SchemaContext context = SchemaContext.Mvc) =>
        new(Identity(baseType), context, SchemaRole.Union);

    public static SchemaKey BaseDefault(Type baseType, SchemaContext context = SchemaContext.Mvc) =>
        new(Identity(baseType), context, SchemaRole.BaseDefault);

    public static SchemaKey Variant(Type derivedType, Type baseType, SchemaContext context = SchemaContext.Mvc) =>
        new(Identity(derivedType), context, SchemaRole.Variant, Identity(baseType));

    /// <summary>The same component in <paramref name="context"/>.</summary>
    public SchemaKey In(SchemaContext context) => this with { Context = context };

    private static string Identity(Type type) => type.FullName ?? type.Name;
}

/// <summary>
/// Hands out every component id of one document, so that two different keys never share an id.
/// </summary>
/// <remarks>
/// <para>
/// A key keeps the id it was given first. A new key gets its preferred candidate when no other
/// key holds it; otherwise the fallback (the full CLR name with <c>.</c> and <c>+</c> replaced by
/// <c>_</c>); when that is taken too, the fallback with a numeric suffix <c>_2</c>, <c>_3</c>, …
/// in reservation order. Generation runs in a deterministic order, so the ids are deterministic.
/// </para>
/// <para>
/// Without a collision the candidate is used as is, so ids that did not collide before keep
/// their names.
/// </para>
/// </remarks>
internal sealed class SchemaIdRegistry
{
    private readonly Dictionary<SchemaKey, string> _idByKey = new();
    private readonly Dictionary<string, SchemaKey> _keyById = new(StringComparer.Ordinal);

    /// <summary>Returns the id of <paramref name="key"/>, reserving one on first use.</summary>
    /// <param name="key">The component to identify.</param>
    /// <param name="candidate">Preferred id.</param>
    /// <param name="fallback">Id used when <paramref name="candidate"/> belongs to another key.</param>
    public string Reserve(SchemaKey key, string candidate, string fallback)
    {
        if (_idByKey.TryGetValue(key, out var existing))
            return existing;

        var id = IsFreeFor(candidate, key) ? candidate : FirstFree(fallback, key);
        _idByKey[key] = id;
        _keyById[id] = key;
        return id;
    }

    /// <summary>The id reserved for <paramref name="key"/>, if any.</summary>
    public bool TryGetId(SchemaKey key, out string id) => _idByKey.TryGetValue(key, out id!);

    private bool IsFreeFor(string id, SchemaKey key) =>
        !_keyById.TryGetValue(id, out var holder) || holder == key;

    private string FirstFree(string fallback, SchemaKey key)
    {
        if (IsFreeFor(fallback, key))
            return fallback;

        for (var suffix = 2; ; suffix++)
        {
            var id = $"{fallback}_{suffix}";
            if (IsFreeFor(id, key))
                return id;
        }
    }
}
