namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>Serialization context a component schema describes.</summary>
internal enum SchemaContext
{
    /// <summary>MVC controller bodies, serialized with the MVC JSON options.</summary>
    Mvc,
}

/// <summary>What a component schema stands for, besides its CLR type.</summary>
internal enum SchemaRole
{
    /// <summary>The type itself, as written when it is used directly.</summary>
    Direct,
}

/// <summary>
/// Identity of one component schema: the CLR type, the serialization context and the role.
/// The type is identified by its full name, so closed generic types with different arguments
/// (or different definitions) are different keys.
/// </summary>
internal readonly record struct SchemaKey(string TypeIdentity, SchemaContext Context, SchemaRole Role)
{
    public static SchemaKey Direct(Type type) =>
        new(type.FullName ?? type.Name, SchemaContext.Mvc, SchemaRole.Direct);
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
