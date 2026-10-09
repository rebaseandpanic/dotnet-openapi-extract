using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.Polymorphism;

namespace ModernApi.Controllers;

/// <summary>Unions with a base branch, with alternatives without a value, nested and recursive.</summary>
[ApiController]
[Route("polymorphism-alt")]
public class PolymorphismAlternativesController : ControllerBase
{
    /// <summary>A pet (concrete base declared among its derived types).</summary>
    [HttpGet("pet")]
    public ActionResult<Pet> GetPet() => throw new NotSupportedException();

    /// <summary>A hamster (direct use).</summary>
    [HttpGet("hamster")]
    public ActionResult<Hamster> GetHamster() => throw new NotSupportedException();

    /// <summary>The DTO named like the base branch of Pet.</summary>
    [HttpGet("pet-default")]
    public ActionResult<PetDefault> GetPetDefault() => throw new NotSupportedException();

    /// <summary>A fish (concrete base, unknown discriminators read as the base).</summary>
    [HttpGet("fish")]
    public ActionResult<Fish> GetFish() => throw new NotSupportedException();

    /// <summary>A gadget (Swashbuckle, a subtype without a value).</summary>
    [HttpGet("gadget")]
    public ActionResult<Gadget> GetGadget() => throw new NotSupportedException();

    /// <summary>A creature (its derived Bird is a base of its own).</summary>
    [HttpGet("creature")]
    public ActionResult<Creature> GetCreature() => throw new NotSupportedException();

    /// <summary>A bird (polymorphic use of the nested base).</summary>
    [HttpGet("bird")]
    public ActionResult<Bird> GetBird() => throw new NotSupportedException();

    /// <summary>A fruit (concrete base with UnknownDerivedTypeHandling).</summary>
    [HttpGet("fruit")]
    public ActionResult<Fruit> GetFruit() => throw new NotSupportedException();

    /// <summary>A gem (concrete base, integer discriminators, unknown values read as the base).</summary>
    [HttpGet("gem")]
    public ActionResult<Gem> GetGem() => throw new NotSupportedException();

    /// <summary>A household (value-less derived type that is a base of its own).</summary>
    [HttpGet("household")]
    public ActionResult<Household> GetHousehold() => throw new NotSupportedException();

    /// <summary>A flat (polymorphic use of the nested base).</summary>
    [HttpGet("flat")]
    public ActionResult<Flat> GetFlat() => throw new NotSupportedException();

    /// <summary>A sensor (the base listed without a value).</summary>
    [HttpGet("sensor")]
    public ActionResult<Sensor> GetSensor() => throw new NotSupportedException();

    /// <summary>A tree node (recursive hierarchy).</summary>
    [HttpGet("tree")]
    public ActionResult<TreeNode> GetTree() => throw new NotSupportedException();
}

/// <summary>Unions reachable only from a path prefix that tests exclude.</summary>
[ApiController]
[Route("polymorphism-excluded")]
public class ExcludedPolymorphismController : ControllerBase
{
    /// <summary>A message (a derived type without a value).</summary>
    [HttpGet("message")]
    public ActionResult<Message> GetMessage() => throw new NotSupportedException();

    /// <summary>A coupon (concrete base, only on this excluded path).</summary>
    [HttpGet("coupon")]
    public ActionResult<Coupon> GetCoupon() => throw new NotSupportedException();

    /// <summary>A ticket (STJ and Swashbuckle disagree).</summary>
    [HttpGet("ticket")]
    public ActionResult<Ticket> GetTicket() => throw new NotSupportedException();
}
