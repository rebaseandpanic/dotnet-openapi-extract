using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace ModernApi.Models.Polymorphism;

/// <summary>A pet; a concrete base declared among its derived types, unknown discriminators rejected.</summary>
[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(Pet), "pet")]
[JsonDerivedType(typeof(Hamster), "hamster")]
public class Pet
{
    /// <summary>Nickname of the pet.</summary>
    public string Nickname { get; set; } = string.Empty;
}

/// <summary>A hamster.</summary>
public sealed class Hamster : Pet
{
    /// <summary>Wheel size in centimetres.</summary>
    public int WheelSize { get; set; }
}

/// <summary>An ordinary DTO whose name equals the base branch name of Pet.</summary>
public sealed class PetDefault
{
    /// <summary>Owner of the pet.</summary>
    public string Owner { get; set; } = string.Empty;
}

/// <summary>A fish; a concrete base not declared among its derived types, unknown discriminators read as the base.</summary>
[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(Shark), "shark")]
public class Fish
{
    /// <summary>Number of fins.</summary>
    public int Fins { get; set; }
}

/// <summary>A shark.</summary>
public sealed class Shark : Fish
{
    /// <summary>Number of teeth.</summary>
    public int Teeth { get; set; }
}

/// <summary>A message; one derived type without a discriminator value.</summary>
[JsonDerivedType(typeof(TextMessage), "text")]
[JsonDerivedType(typeof(ImageMessage))]
public class Message
{
    /// <summary>Sender of the message.</summary>
    public string Sender { get; set; } = string.Empty;
}

/// <summary>A text message.</summary>
public sealed class TextMessage : Message
{
    /// <summary>Text of the message.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>An image message.</summary>
public sealed class ImageMessage : Message
{
    /// <summary>Image URL.</summary>
    public string Url { get; set; } = string.Empty;
}

/// <summary>A gadget; Swashbuckle attributes, one subtype without a discriminator value.</summary>
[SwaggerSubType(typeof(Phone), DiscriminatorValue = "phone")]
[SwaggerSubType(typeof(Watch))]
public abstract class Gadget
{
    /// <summary>Brand name.</summary>
    public string Brand { get; set; } = string.Empty;
}

/// <summary>A phone.</summary>
public sealed class Phone : Gadget
{
    /// <summary>Phone number.</summary>
    public string Number { get; set; } = string.Empty;
}

/// <summary>A watch.</summary>
public sealed class Watch : Gadget
{
    /// <summary>Whether it is waterproof.</summary>
    public bool Waterproof { get; set; }
}

/// <summary>A ticket; STJ and Swashbuckle disagree on the value.</summary>
[JsonDerivedType(typeof(BusTicket), "bus")]
[SwaggerSubType(typeof(BusTicket), DiscriminatorValue = "BUS")]
public abstract class Ticket
{
    /// <summary>Ticket code.</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>A bus ticket.</summary>
public sealed class BusTicket : Ticket
{
    /// <summary>Bus route.</summary>
    public string Route { get; set; } = string.Empty;
}

/// <summary>A creature; its derived Bird is itself a polymorphic base.</summary>
[JsonDerivedType(typeof(Bird), "bird")]
public abstract class Creature
{
    /// <summary>Habitat.</summary>
    public string Habitat { get; set; } = string.Empty;
}

/// <summary>A bird; a polymorphic base of its own.</summary>
[JsonDerivedType(typeof(Parrot), "parrot")]
public abstract class Bird : Creature
{
    /// <summary>Wing span in centimetres.</summary>
    public int Wings { get; set; }
}

/// <summary>A parrot.</summary>
public sealed class Parrot : Bird
{
    /// <summary>Words it knows.</summary>
    public int Words { get; set; }
}

/// <summary>A tree node; recursive through its branch.</summary>
[JsonDerivedType(typeof(Leaf), "leaf")]
[JsonDerivedType(typeof(Branch), "branch")]
public abstract class TreeNode
{
    /// <summary>Node label.</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>A leaf.</summary>
public sealed class Leaf : TreeNode
{
    /// <summary>Leaf value.</summary>
    public int Value { get; set; }
}

/// <summary>A branch holding further nodes.</summary>
public sealed class Branch : TreeNode
{
    /// <summary>Child nodes.</summary>
    public List<TreeNode> Children { get; set; } = [];
}

/// <summary>A fruit; a concrete base whose unknown CLR types fall back to an ancestor when written.</summary>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(Apple), "apple")]
public class Fruit
{
    /// <summary>Weight in grams.</summary>
    public int Weight { get; set; }
}

/// <summary>An apple.</summary>
public sealed class Apple : Fruit
{
    /// <summary>Apple variety.</summary>
    public string Variety { get; set; } = string.Empty;
}

/// <summary>A coupon; a concrete base reachable only from an excluded path.</summary>
[JsonDerivedType(typeof(PercentCoupon), "percent")]
public class Coupon
{
    /// <summary>Coupon code.</summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>A percentage coupon.</summary>
public sealed class PercentCoupon : Coupon
{
    /// <summary>Discount percentage.</summary>
    public int Percent { get; set; }
}

/// <summary>A gem; a concrete base with integer discriminators, unknown discriminators read as the base.</summary>
[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(Ruby), 1)]
public class Gem
{
    /// <summary>Carats.</summary>
    public int Carats { get; set; }
}

/// <summary>A ruby.</summary>
public sealed class Ruby : Gem
{
    /// <summary>Colour depth.</summary>
    public int Depth { get; set; }
}

/// <summary>A household; its only derived type has no discriminator value and is a polymorphic base itself.</summary>
[JsonDerivedType(typeof(Flat))]
public class Household
{
    /// <summary>Number of rooms.</summary>
    public int Rooms { get; set; }
}

/// <summary>A flat; a polymorphic base of its own.</summary>
[JsonDerivedType(typeof(Penthouse), "penthouse")]
public class Flat : Household
{
    /// <summary>Floor number.</summary>
    public int Floor { get; set; }
}

/// <summary>A penthouse.</summary>
public sealed class Penthouse : Flat
{
    /// <summary>Terrace area.</summary>
    public int Terrace { get; set; }
}

/// <summary>A sensor; the base itself is listed among its derived types without a value.</summary>
[JsonDerivedType(typeof(Sensor))]
[JsonDerivedType(typeof(Thermometer), "thermo")]
public class Sensor
{
    /// <summary>Sensor identifier.</summary>
    public string SerialNumber { get; set; } = string.Empty;
}

/// <summary>A thermometer.</summary>
public sealed class Thermometer : Sensor
{
    /// <summary>Last reading.</summary>
    public double Celsius { get; set; }
}
