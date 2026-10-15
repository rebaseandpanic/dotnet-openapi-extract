using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Swashbuckle.AspNetCore.Annotations;

namespace ModernApi.Models.Keywords;

/// <summary>Sibling target component.</summary>
public class SiblingTarget
{
    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>Sibling point component, a struct: its nullable form is a nullable reference.</summary>
public struct SiblingPoint
{
    /// <summary>X.</summary>
    public int X { get; set; }
}

/// <summary>
/// Properties that reference a component and carry keywords of their own: one class of sibling
/// keyword per property, the forms with allowed values, nullable references, and a bare reference.
/// </summary>
/// <remarks>
/// Properties without an XML <c>&lt;summary&gt;</c> keep the description out of the keyword set under test.
/// </remarks>
public class RefSiblingsModel
{
    [Description("place-description")]
    public SiblingTarget Described { get; set; } = new();

    [SwaggerSchema(Title = "place-title")]
    public SiblingTarget Titled { get; set; } = new();

    [DefaultValue("place-default")]
    public SiblingTarget Defaulted { get; set; } = new();

    /// <example>{"name":"place-example"}</example>
    public SiblingTarget Exemplified { get; set; } = new();

    // A reference with a sibling and no XML <summary>.
    [Obsolete("Use Described.")]
    public SiblingTarget Obsoleted { get; set; } = new();

    [SwaggerSchema(ReadOnly = true)]
    public SiblingTarget ReadOnlyTarget { get; set; } = new();

    [SwaggerSchema(WriteOnly = true)]
    public SiblingTarget WriteOnlyTarget { get; set; } = new();

    [MinLength(1), MaxLength(5)]
    public SiblingTarget Lengths { get; set; } = new();

    [RegularExpression("^place$")]
    public SiblingTarget Patterned { get; set; } = new();

    [SwaggerSchema(Format = "place-format")]
    public SiblingTarget Formatted { get; set; } = new();

    [AllowedValues("low", "high"), DeniedValues("mid")]
    public SiblingTarget Allowed { get; set; } = new();

    [AllowedValues("low")]
    public SiblingTarget AllowedSingle { get; set; } = new();

    [Description("nullable-description"), MaxLength(3)]
    public SiblingTarget? NullableDescribed { get; set; }

    [Description("nullable-allowed"), AllowedValues("low", "high"), DeniedValues("mid")]
    public SiblingTarget? NullableAllowed { get; set; }

    [AllowedValues("low")]
    public SiblingTarget? NullableAllowedSingle { get; set; }

    public SiblingTarget Bare { get; set; } = new();

    /// <summary>xml-description</summary>
    public SiblingTarget XmlDescribed { get; set; } = new();

    /// <summary>xml-wins</summary>
    [Description("attribute-loses")]
    public SiblingTarget XmlOverAttribute { get; set; } = new();
}

/// <summary>Another place of the same component, without keywords of its own.</summary>
public class RefSiblingsNeighbour
{
    public SiblingTarget Target { get; set; } = new();
}
