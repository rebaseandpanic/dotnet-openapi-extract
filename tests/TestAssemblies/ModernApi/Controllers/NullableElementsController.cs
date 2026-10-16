using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.NullableElements;
using ModernApi.Models.Streaming;

namespace ModernApi.Controllers;

/// <summary>Parameters, bodies and responses whose collections, arrays and dictionaries have nullable elements.</summary>
[ApiController]
[Route("nullable-elements")]
public class NullableElementsController : ControllerBase
{
    private static readonly List<ElementItem?> Items = [new() { Name = "item" }, null];

    private static readonly List<TypedElementItem?> TypedItems = [new() { Name = "item" }, null];

    // DTOs.

    /// <summary>Properties with nullable elements and values.</summary>
    [HttpGet("model")]
    public NullableElementsModel Model() => new();

    /// <summary>The same BCL JSON types without annotations.</summary>
    [HttpGet("json-elements")]
    public JsonElementsModel JsonElements() => new();

    /// <summary>One component in three places.</summary>
    [HttpGet("item-places")]
    public ElementItemPlaces ItemPlaces() => new();

    /// <summary>Collections declared without nullable context.</summary>
    [HttpGet("oblivious")]
    public ObliviousElementsModel Oblivious() => new();

    // Query parameters: a collection, and the nested generic and dictionary forms bound by [FromQuery].

    /// <summary>A query collection of nullable strings.</summary>
    [HttpGet("query")]
    public IActionResult Query(
        [FromQuery] List<string?> tags,
        [FromQuery] List<List<string?>?> nested,
        [FromQuery] Dictionary<string, string?> map) => Ok();

    // Request bodies.

    /// <summary>A body of nullable items.</summary>
    [HttpPost("body")]
    public IActionResult Body([FromBody] List<ElementItem?> body) => Ok();

    /// <summary>A body of nullable item values.</summary>
    [HttpPost("body-dictionary")]
    public IActionResult BodyDictionary([FromBody] Dictionary<string, ElementItem?> body) => Ok();

    /// <summary>A body of nullable lists of nullable items.</summary>
    [HttpPost("body-nested")]
    public IActionResult BodyNested([FromBody] List<List<ElementItem?>?> body) => Ok();

    // Form fields: a collection, and the nested generic and dictionary forms bound by [FromForm].

    /// <summary>Form fields with nullable elements and values.</summary>
    [HttpPost("form")]
    public IActionResult Form(
        [FromForm] List<string?> names,
        [FromForm] List<List<string?>?> nested,
        [FromForm] Dictionary<string, ElementItem?> map) => Ok();

    // Responses from the signature.

    /// <summary>A response of nullable items.</summary>
    [HttpGet("response")]
    public List<ElementItem?> Response() => Items;

    /// <summary>A response of nullable items inside a task.</summary>
    [HttpGet("response-task")]
    public Task<List<ElementItem?>> ResponseTask() => Task.FromResult(Items);

    /// <summary>A response of nullable items inside an action result.</summary>
    [HttpGet("response-action-result")]
    public ActionResult<List<ElementItem?>> ResponseActionResult() => Items;

    /// <summary>A typed result of nullable items.</summary>
    [HttpGet("response-ok")]
    public Ok<List<TypedElementItem?>> ResponseOk() => TypedResults.Ok(TypedItems);

    /// <summary>A union whose first variant has nullable items.</summary>
    [HttpGet("response-union")]
    public Results<Ok<List<TypedElementItem?>>, NotFound> ResponseUnion() => TypedResults.Ok(TypedItems);

    /// <summary>A union with the same CLR body type in a non-nullable and a nullable position, the nullable one second.</summary>
    [HttpPost("response-union-distinct")]
    public Results<Ok<List<TypedElementItem>>, Created<List<TypedElementItem?>>> ResponseUnionDistinct() =>
        TypedResults.Created("/nullable-elements/response", TypedItems);

    /// <summary>A sequence of lists of nullable items as a JSON array.</summary>
    [HttpGet("response-stream")]
    [Produces("application/json")]
    public IAsyncEnumerable<List<ElementItem?>> ResponseStream() => Lists();

    /// <summary>A sequence of lists of nullable items as JSON Lines.</summary>
    [HttpGet("response-stream-jsonl")]
    [Produces("application/jsonl")]
    public IAsyncEnumerable<List<ElementItem?>> ResponseStreamJsonl() => Lists();

    /// <summary>A response of nullable item values.</summary>
    [HttpGet("response-dictionary")]
    public Dictionary<string, ElementItem?> ResponseDictionary() => new() { ["item"] = null };

    /// <summary>A response of nullable lists of nullable items.</summary>
    [HttpGet("response-nested")]
    public List<List<ElementItem?>?> ResponseNested() => [Items, null];

    /// <summary>A 200 response declared without a type: its body is the return type of nullable items.</summary>
    [HttpGet("response-declared")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public List<ElementItem?> ResponseDeclared() => Items;

    // Responses whose body type is declared by an attribute.

    /// <summary>The attribute declares the body type; the signature's nullable elements do not apply to it.</summary>
    [HttpGet("response-attribute")]
    [ProducesResponseType(typeof(List<ElementItem>), StatusCodes.Status200OK)]
    public List<ElementItem?> ResponseAttribute() => Items;

    /// <summary>The attribute declares the sequence type; the signature's nullable elements do not apply to it.</summary>
    [HttpGet("response-attribute-stream")]
    [Produces(typeof(IAsyncEnumerable<StreamItem>))]
    public IAsyncEnumerable<StreamItem?> ResponseAttributeStream() => StreamItems();

    private static async IAsyncEnumerable<List<ElementItem?>> Lists()
    {
        await Task.Yield();
        yield return Items;
    }

    private static async IAsyncEnumerable<StreamItem?> StreamItems()
    {
        await Task.Yield();
        yield return null;
    }
}
