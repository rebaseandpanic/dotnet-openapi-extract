using System.Text.Json;
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace ModernApi.Models.Polymorphism;

/// <summary>An animal; abstract, every derived type declared with a string discriminator.</summary>
[JsonDerivedType(typeof(Cat), "cat")]
[JsonDerivedType(typeof(Dog), "dog")]
public abstract class Animal
{
    /// <summary>Name of the animal.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>A cat.</summary>
public sealed class Cat : Animal
{
    /// <summary>Lives left.</summary>
    public int Lives { get; set; }
}

/// <summary>A dog.</summary>
public sealed class Dog : Animal
{
    /// <summary>Breed of the dog.</summary>
    public string Breed { get; set; } = string.Empty;
}

/// <summary>An ordinary DTO whose name equals the variant name of Cat in Animal.</summary>
public sealed class CatAsAnimal
{
    /// <summary>Who adopted the cat.</summary>
    public string AdoptedBy { get; set; } = string.Empty;
}

/// <summary>A shape; an interface with integer discriminators under a custom property name.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Circle), 1)]
[JsonDerivedType(typeof(Square), 2)]
public interface IShape
{
    /// <summary>Area of the shape.</summary>
    double Area { get; }
}

/// <summary>A circle.</summary>
public sealed class Circle : IShape
{
    /// <summary>Radius.</summary>
    public double Radius { get; set; }

    /// <inheritdoc />
    public double Area => Math.PI * Radius * Radius;
}

/// <summary>A square.</summary>
public sealed class Square : IShape
{
    /// <summary>Side length.</summary>
    public double Side { get; set; }

    /// <inheritdoc />
    public double Area => Side * Side;
}

/// <summary>A vehicle; polymorphism declared with Swashbuckle attributes only.</summary>
[SwaggerDiscriminator("vehicleType")]
[SwaggerSubType(typeof(Car), DiscriminatorValue = "car")]
[SwaggerSubType(typeof(Bike), DiscriminatorValue = "bike")]
public abstract class Vehicle
{
    /// <summary>Number of wheels.</summary>
    public int Wheels { get; set; }
}

/// <summary>A car.</summary>
public sealed class Car : Vehicle
{
    /// <summary>Number of seats.</summary>
    public int Seats { get; set; }
}

/// <summary>A bike.</summary>
public sealed class Bike : Vehicle
{
    /// <summary>Whether it has a bell.</summary>
    public bool HasBell { get; set; }
}

/// <summary>A payment; STJ and Swashbuckle attributes disagree, STJ defines the wire.</summary>
[JsonDerivedType(typeof(CardPayment), "card")]
[JsonDerivedType(typeof(CashPayment), "cash")]
[SwaggerDiscriminator("method")]
[SwaggerSubType(typeof(CardPayment), DiscriminatorValue = "CARD")]
[SwaggerSubType(typeof(CashPayment), DiscriminatorValue = "CASH")]
public abstract class Payment
{
    /// <summary>Amount paid.</summary>
    public decimal Amount { get; set; }
}

/// <summary>A card payment.</summary>
public sealed class CardPayment : Payment
{
    /// <summary>Masked card number.</summary>
    public string Card { get; set; } = string.Empty;
}

/// <summary>A cash payment.</summary>
public sealed class CashPayment : Payment
{
    /// <summary>Change given.</summary>
    public decimal Change { get; set; }
}

/// <summary>A document with its own converter: the converter owns the wire, no union is written.</summary>
[JsonConverter(typeof(DocumentConverter))]
[JsonDerivedType(typeof(Invoice), "invoice")]
public abstract class Document
{
    /// <summary>Document number.</summary>
    public string Number { get; set; } = string.Empty;
}

/// <summary>An invoice.</summary>
public sealed class Invoice : Document
{
    /// <summary>Total amount.</summary>
    public decimal Total { get; set; }
}

/// <summary>Custom converter for <see cref="Document"/>.</summary>
public sealed class DocumentConverter : JsonConverter<Document>
{
    /// <inheritdoc />
    public override Document Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Document value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Number);
}
