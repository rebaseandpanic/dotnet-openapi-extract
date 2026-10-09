using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>Where the polymorphism configuration of a base type was read from.</summary>
internal enum PolymorphismSource
{
    /// <summary><c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c>: what System.Text.Json writes.</summary>
    SystemTextJson,

    /// <summary><c>[SwaggerDiscriminator]</c> / <c>[SwaggerSubType]</c>, used when no STJ attribute is present.</summary>
    Swashbuckle,
}

/// <summary>One declared derived type and its discriminator value (string, int, or null when declared without one).</summary>
internal sealed record DerivedTypeInfo(Type Type, object? DiscriminatorValue);

/// <summary>The polymorphism configuration declared on one base type.</summary>
internal sealed record PolymorphismInfo(
    Type BaseType,
    string PropertyName,
    bool IgnoreUnrecognizedTypeDiscriminators,
    IReadOnlyList<DerivedTypeInfo> DerivedTypes,
    PolymorphismSource Source,
    bool SwashbuckleDisagrees = false);

/// <summary>
/// Reads the polymorphism a base type declares. System.Text.Json attributes define the wire, so
/// when they are present they are the source; Swashbuckle's attributes are read only without them.
/// The configuration is the type's own: attributes are not inherited, so a derived type is
/// polymorphic only through its own declarations. A base with a <c>[JsonConverter]</c> is not
/// polymorphic for the schema: the converter owns its wire format.
/// </summary>
internal static class PolymorphismReader
{
    /// <summary>The STJ default discriminator property name.</summary>
    public const string DefaultPropertyName = "$type";

    public static PolymorphismInfo? Read(Type baseType)
    {
        IList<System.Reflection.CustomAttributeData> attributes;
        try
        {
            attributes = baseType.GetCustomAttributesData();
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            return null;
        }

        if (AttributeHelper.HasAttribute(attributes, AttributeHelper.Names.JsonConverter))
            return null;

        var stjDerived = AttributeHelper.GetAttributes(attributes, AttributeHelper.Names.JsonDerivedType)
            .Select(ReadJsonDerivedType)
            .Where(d => d != null)
            .Select(d => d!)
            .ToList();

        if (stjDerived.Count > 0)
        {
            var polymorphic = AttributeHelper.GetAttribute(attributes, AttributeHelper.Names.JsonPolymorphic);
            var propertyName = polymorphic != null
                ? AttributeHelper.GetNamedArgument<string>(polymorphic, "TypeDiscriminatorPropertyName")
                : null;
            var ignoreUnrecognized = polymorphic != null
                && AttributeHelper.GetNamedArgument<bool>(polymorphic, "IgnoreUnrecognizedTypeDiscriminators");

            var stjProperty = string.IsNullOrEmpty(propertyName) ? DefaultPropertyName : propertyName;
            var swashbuckle = ReadSwashbuckle(baseType, attributes);
            var disagrees = swashbuckle != null
                && (swashbuckle.PropertyName != stjProperty
                    || !swashbuckle.DerivedTypes.Select(Describe).ToHashSet().SetEquals(stjDerived.Select(Describe)));

            return new PolymorphismInfo(
                baseType,
                stjProperty,
                ignoreUnrecognized,
                stjDerived,
                PolymorphismSource.SystemTextJson,
                disagrees);
        }

        return ReadSwashbuckle(baseType, attributes);
    }

    /// <summary>
    /// A derived type with its value, the value's type included: the integer <c>1</c> and the
    /// string <c>"1"</c> are different discriminators on the wire.
    /// </summary>
    private static string Describe(DerivedTypeInfo derived) => derived.DiscriminatorValue switch
    {
        null       => $"{derived.Type.FullName}=(none)",
        int number => $"{derived.Type.FullName}=int:{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        var value  => $"{derived.Type.FullName}=string:{value}",
    };

    private static PolymorphismInfo? ReadSwashbuckle(Type baseType, IList<System.Reflection.CustomAttributeData> attributes)
    {
        var swaggerDerived = AttributeHelper.GetAttributes(attributes, AttributeHelper.Names.SwaggerSubType)
            .Select(ReadSwaggerSubType)
            .Where(d => d != null)
            .Select(d => d!)
            .ToList();

        if (swaggerDerived.Count == 0)
            return null;

        var discriminator = AttributeHelper.GetAttribute(attributes, AttributeHelper.Names.SwaggerDiscriminator);
        var swaggerProperty = discriminator != null
            ? AttributeHelper.GetConstructorArgument<string>(discriminator, 0)
            : null;

        return new PolymorphismInfo(
            baseType,
            string.IsNullOrEmpty(swaggerProperty) ? DefaultPropertyName : swaggerProperty,
            IgnoreUnrecognizedTypeDiscriminators: false,
            swaggerDerived,
            PolymorphismSource.Swashbuckle);
    }

    /// <summary><c>[JsonDerivedType(typeof(D))]</c>, <c>(typeof(D), "value")</c> or <c>(typeof(D), 1)</c>.</summary>
    private static DerivedTypeInfo? ReadJsonDerivedType(System.Reflection.CustomAttributeData attribute)
    {
        if (attribute.ConstructorArguments.Count == 0 || attribute.ConstructorArguments[0].Value is not Type derived)
            return null;

        var value = attribute.ConstructorArguments.Count > 1 ? attribute.ConstructorArguments[1].Value : null;
        return new DerivedTypeInfo(derived, value is string or int ? value : null);
    }

    /// <summary><c>[SwaggerSubType(typeof(D), DiscriminatorValue = "value")]</c>.</summary>
    private static DerivedTypeInfo? ReadSwaggerSubType(System.Reflection.CustomAttributeData attribute)
    {
        if (attribute.ConstructorArguments.Count == 0 || attribute.ConstructorArguments[0].Value is not Type derived)
            return null;

        var value = AttributeHelper.GetNamedArgument<string>(attribute, "DiscriminatorValue");
        return new DerivedTypeInfo(derived, string.IsNullOrEmpty(value) ? null : value);
    }
}
