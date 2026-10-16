using System.Reflection;
using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// The schema of one place where a component is referenced (a DTO property, a parameter, a body, a
/// form field), built in a working form and finished in the form of the target version.
/// </summary>
/// <remarks>
/// <para>
/// The appliers of constraints, examples, defaults and allowed values write to an
/// <see cref="OpenApiSchema"/>, so while a place is built its keywords sit on one of two working forms:
/// </para>
/// <list type="bullet">
/// <item><b>P-WRAP</b>: the <c>allOf</c> wrapper of a reference, <c>{allOf: [$ref, constraint?], keywords…}</c>,
/// where the optional constraint is the <c>{enum}</c> / <c>{const}</c> of <c>[AllowedValues]</c>;</item>
/// <item><b>P-NULLC</b>: an outer <c>allOf</c> around the nullable form of a reference and an allowed-values
/// constraint, <c>{allOf: [{anyOf: [$ref, {type: "null"}], keywords…}, {enum|const}], not?, keywords…}</c>;
/// the nullable form of an enum, <c>anyOf: [enum schema, {type: "null"}]</c>, takes the place of the
/// reference's in the same way.</item>
/// </list>
/// <para>
/// <see cref="Finish"/> runs after the last writer and reader of the place. OpenAPI 3.0 ignores the
/// siblings of <c>$ref</c>, so there the working form is the output. In 3.1 and 3.2 (JSON Schema
/// 2020-12) the siblings of <c>$ref</c> apply together with it, as the elements of <c>allOf</c> do:
/// P-WRAP becomes the reference with the same keywords in its own fields (<c>Reference.*</c>), P-NULLC
/// the <c>anyOf</c> schema with the keywords and the constraint beside <c>anyOf</c>. The set of valid
/// instances and the set of keywords with their values stay the same; the referenced component is
/// never touched.
/// </para>
/// </remarks>
internal static class ReferencePlace
{
    /// <summary>The keywords of a schema: every settable property but <c>allOf</c>, which is the form itself.</summary>
    private static readonly PropertyInfo[] SchemaKeywords = typeof(OpenApiSchema)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.Name != nameof(OpenApiSchema.AllOf))
        .ToArray();

    /// <summary>The sibling fields of a reference, by keyword property name.</summary>
    private static readonly Dictionary<string, PropertyInfo> ReferenceFields = typeof(JsonSchemaReference)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
        .ToDictionary(p => p.Name, StringComparer.Ordinal);

    /// <summary>
    /// The keyword fields a reference may carry next to <c>$ref</c> in a document read from a file, each
    /// with the schema keyword of the same name, <c>allOf</c> included: what a place reads as its own
    /// keywords. The fields of <see cref="BaseOpenApiReference"/> (<c>Id</c>, <c>Type</c>, …) say what the
    /// reference points to and are not siblings of <c>$ref</c>.
    /// </summary>
    private static readonly (PropertyInfo Field, PropertyInfo Keyword)[] SiblingFields = ReferenceFields.Values
        .Where(field => field.DeclaringType != typeof(BaseOpenApiReference))
        .Select(field => (Field: field, Keyword: typeof(OpenApiSchema).GetProperty(field.Name, BindingFlags.Public | BindingFlags.Instance)))
        .Where(pair => pair.Keyword is { CanRead: true, CanWrite: true })
        .Where(pair => pair.Keyword is not null && ValueType(pair.Keyword).IsAssignableFrom(ValueType(pair.Field)))
        .Select(pair => (pair.Field, pair.Keyword!))
        .ToArray();

    /// <summary>An untouched schema: a keyword is set when its value differs from this one's.</summary>
    private static readonly OpenApiSchema Unset = new();

    /// <summary>
    /// The working form of a place that keywords are written to: <paramref name="schema"/> itself when it
    /// is an <see cref="OpenApiSchema"/>, the P-WRAP wrapper <c>{allOf: [$ref]}</c> of a reference.
    /// </summary>
    public static OpenApiSchema Mutable(IOpenApiSchema schema) => schema switch
    {
        OpenApiSchema inline => inline,
        OpenApiSchemaReference reference => new OpenApiSchema { AllOf = [reference] },
        _ => new OpenApiSchema(),
    };

    /// <summary>
    /// The schema of the place in the form of <paramref name="version"/>: unchanged for 3.0 and for a
    /// schema in neither working form; for 3.1 and 3.2 P-WRAP becomes the reference with its keywords in
    /// <c>Reference.*</c>, P-NULLC the <c>anyOf</c> schema with the keywords beside it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A keyword of the working form has no field next to <c>$ref</c> (<c>type</c> never has one: a
    /// nullable reference is an <c>anyOf</c>), or is set twice.
    /// </exception>
    public static IOpenApiSchema Finish(IOpenApiSchema schema, OpenApiSpecVersion version)
    {
        if (version == OpenApiSpecVersion.OpenApi3_0 || schema is not OpenApiSchema place)
            return schema;

        if (place.AllOf is { Count: > 0 } wrap
            && wrap[0] is OpenApiSchemaReference reference
            && wrap.Skip(1).All(IsValueConstraint))
        {
            foreach (var keyword in KeywordsSetOn(place))
                SetOnReference(reference.Reference, keyword, keyword.GetValue(place));
            foreach (var wrapConstraint in wrap.Skip(1).Cast<OpenApiSchema>())
            foreach (var keyword in KeywordsSetOn(wrapConstraint))
                SetOnReference(reference.Reference, keyword, keyword.GetValue(wrapConstraint));
            return reference;
        }

        if (place.AllOf is [OpenApiSchema union, OpenApiSchema constraint]
            && IsNullableReference(union)
            && IsValueConstraint(constraint))
        {
            foreach (var keyword in KeywordsSetOn(place))
                SetOnSchema(union, keyword, keyword.GetValue(place));
            foreach (var keyword in KeywordsSetOn(constraint))
                SetOnSchema(union, keyword, keyword.GetValue(constraint));
            return union;
        }

        return schema;
    }

    /// <summary>
    /// The keywords a place itself carries, in one form whatever version wrote it, for the rules that
    /// check a place: an <see cref="OpenApiSchema"/> as it is, except that the allowed values a 3.0 wrapper
    /// keeps in an element of its <c>allOf</c> are read beside its other keywords; a
    /// reference with keywords of its own beside <c>$ref</c> (3.1+) as the equivalent wrapper
    /// <c>{allOf: [$ref], keywords…}</c> built from <c>Reference.*</c> (an <c>allOf</c> of its own joins the
    /// wrapper's elements after <c>$ref</c>); a bare reference as <c>null</c>,
    /// since it has no keywords of its own.
    /// </summary>
    /// <remarks>
    /// The referenced component is never read: an <see cref="OpenApiSchemaReference"/> falls back to its
    /// target for a keyword it does not set, so only the fields of <c>Reference</c> are taken.
    /// </remarks>
    public static OpenApiSchema? OwnSiblings(IOpenApiSchema schema)
    {
        switch (schema)
        {
            case OpenApiSchema place:
                return WithAllowedValues(place);

            case OpenApiSchemaReference reference:
                OpenApiSchema? wrapper = null;
                foreach (var (field, keyword) in SiblingFields)
                {
                    if (field.GetValue(reference.Reference) is not { } value) continue;
                    wrapper ??= new OpenApiSchema { AllOf = [reference] };
                    if (keyword.Name == nameof(OpenApiSchema.AllOf))
                        // An allOf of the reference's own applies together with $ref, as the elements of the wrapper do.
                        wrapper.AllOf = [reference, .. (IList<IOpenApiSchema>)value];
                    else
                        keyword.SetValue(wrapper, value);
                }
                return wrapper;

            default:
                return null;
        }
    }

    /// <summary>
    /// A place in a working form read with the allowed values beside its other keywords, as
    /// <see cref="Finish"/> sets them in 3.1+: the <c>enum</c> / <c>const</c> of <c>[AllowedValues]</c>, which
    /// the 3.0 output keeps as an element of <c>allOf</c> after the reference (or after its nullable
    /// <c>anyOf</c>), is taken onto a shallow copy; <paramref name="place"/> itself is not changed. A keyword
    /// the place already sets keeps its value. A nullable enum without values of its own is read with the
    /// values of its enum branch. Any other schema is returned as it is.
    /// </summary>
    private static OpenApiSchema WithAllowedValues(OpenApiSchema place)
    {
        // A nullable enum: the values of the place are those of its enum branch, unless the place has
        // values of its own (the allowed values beside anyOf in 3.1+).
        if (VersionedSchemaForms.NullableEnumBranch(place) is { } enumBranch)
        {
            if (place.Enum is not null || place.Const is not null)
                return place;
            var values = (OpenApiSchema)place.CreateShallowCopy();
            values.Enum = enumBranch.Enum;
            return values;
        }

        if (place.AllOf is not { Count: > 1 } allOf || !IsReferenceForm(allOf[0]))
            return place;

        var constraints = allOf.Skip(1).Where(IsValueConstraint).Cast<OpenApiSchema>().ToList();
        if (constraints.Count == 0)
            return place;

        var view = (OpenApiSchema)place.CreateShallowCopy();
        foreach (var constraint in constraints)
        {
            if (constraint.Enum is not null && view.Enum is null) view.Enum = constraint.Enum;
            if (constraint.Const is not null && view.Const is null) view.Const = constraint.Const;
        }
        return view;
    }

    /// <summary>
    /// The first element of a working form: the reference, the <c>anyOf</c> of its nullable form, or the
    /// <c>anyOf</c> of a nullable enum, which stands where the nullable reference stands.
    /// </summary>
    private static bool IsReferenceForm(IOpenApiSchema schema) =>
        schema is OpenApiSchemaReference
        || schema is OpenApiSchema { AnyOf: { } anyOf } && anyOf.Any(branch => branch is OpenApiSchemaReference)
        || VersionedSchemaForms.NullableEnumBranch(schema) != null;

    /// <summary>
    /// Writes the XML <c>&lt;summary&gt;</c> <paramref name="description"/> to a finished place and returns
    /// the place. An inline schema, and a reference in 3.1+ (its own <c>Reference.Description</c>), take it
    /// when they have no description or the one of the attributes (<paramref name="attributeDescription"/>):
    /// the XML replaces it. A reference in 3.0 is wrapped first, since 3.0 ignores the siblings of
    /// <c>$ref</c>.
    /// </summary>
    public static IOpenApiSchema SetDescription(
        IOpenApiSchema schema, string description, string? attributeDescription, OpenApiSpecVersion version)
    {
        switch (schema)
        {
            case OpenApiSchemaReference reference when version != OpenApiSpecVersion.OpenApi3_0:
                if (string.IsNullOrEmpty(reference.Reference.Description) || reference.Reference.Description == attributeDescription)
                    reference.Reference.Description = description;
                return reference;

            case OpenApiSchemaReference reference:
                var wrapped = Mutable(reference);
                wrapped.Description = description;
                return wrapped;

            case OpenApiSchema inline when string.IsNullOrEmpty(inline.Description) || inline.Description == attributeDescription:
                inline.Description = description;
                return inline;

            default:
                return schema;
        }
    }

    /// <summary>The <c>{enum}</c> or <c>{const}</c> of <c>[AllowedValues]</c>, and nothing else.</summary>
    private static bool IsValueConstraint(IOpenApiSchema schema) =>
        schema is OpenApiSchema { AllOf: null } constraint
        && KeywordsSetOn(constraint).Select(p => p.Name).ToList() is [nameof(OpenApiSchema.Enum)] or [nameof(OpenApiSchema.Const)];

    /// <summary>
    /// The 3.1+ nullable form of a reference, an untyped <c>anyOf: [$ref, {type: "null"}]</c>, or of an
    /// enum, <c>anyOf: [enum schema, {type: "null"}]</c> (<see cref="VersionedSchemaForms.NullableEnum"/>).
    /// </summary>
    private static bool IsNullableReference(OpenApiSchema schema) =>
        schema is { Type: null, AllOf: null, AnyOf: [OpenApiSchemaReference, OpenApiSchema { Type: JsonSchemaType.Null }] }
        || VersionedSchemaForms.NullableEnumBranch(schema) != null;

    private static IEnumerable<PropertyInfo> KeywordsSetOn(OpenApiSchema schema) =>
        SchemaKeywords.Where(p => !Equals(p.GetValue(schema), p.GetValue(Unset)));

    private static void SetOnReference(JsonSchemaReference reference, PropertyInfo keyword, object? value)
    {
        if (!ReferenceFields.TryGetValue(keyword.Name, out var field)
            || !ValueType(field).IsAssignableFrom(ValueType(keyword)))
            throw new InvalidOperationException(
                $"The keyword '{keyword.Name}' of a reference place has no field next to $ref.");
        if (field.GetValue(reference) is not null)
            throw new InvalidOperationException(
                $"The keyword '{keyword.Name}' of a reference place is set both beside the reference and on it.");
        field.SetValue(reference, value);
    }

    private static Type ValueType(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

    private static void SetOnSchema(OpenApiSchema schema, PropertyInfo keyword, object? value)
    {
        if (!Equals(keyword.GetValue(schema), keyword.GetValue(Unset)))
            throw new InvalidOperationException(
                $"The keyword '{keyword.Name}' of a nullable reference place is set both on the outer allOf and beside anyOf.");
        keyword.SetValue(schema, value);
    }
}
