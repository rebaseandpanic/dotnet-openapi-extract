using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Polymorphism;

namespace ModernApi.Controllers;

/// <summary>Polymorphic and direct uses of the polymorphism fixtures.</summary>
[ApiController]
[Route("polymorphism")]
public class PolymorphismController : ControllerBase
{
    /// <summary>An animal (polymorphic use).</summary>
    [HttpGet("animal")]
    public ActionResult<Animal> GetAnimal() => throw new NotSupportedException();

    /// <summary>A cat (direct use).</summary>
    [HttpGet("cat")]
    public ActionResult<Cat> GetCat() => throw new NotSupportedException();

    /// <summary>The DTO named like the variant of Cat in Animal.</summary>
    [HttpGet("cat-as-animal")]
    public ActionResult<CatAsAnimal> GetCatAsAnimal() => throw new NotSupportedException();

    /// <summary>A shape (interface, integer discriminators).</summary>
    [HttpGet("shape")]
    public ActionResult<IShape> GetShape() => throw new NotSupportedException();

    /// <summary>A vehicle (Swashbuckle attributes).</summary>
    [HttpGet("vehicle")]
    public ActionResult<Vehicle> GetVehicle() => throw new NotSupportedException();

    /// <summary>A payment (STJ and Swashbuckle attributes).</summary>
    [HttpGet("payment")]
    public ActionResult<Payment> GetPayment() => throw new NotSupportedException();

    /// <summary>A document (custom converter on the base).</summary>
    [HttpGet("document")]
    public ActionResult<Document> GetDocument() => throw new NotSupportedException();
}
