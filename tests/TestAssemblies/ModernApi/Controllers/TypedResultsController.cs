using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ModernApi.Models.TypedResults;

namespace ModernApi.Controllers;

/// <summary>A result that writes something ASP.NET Core cannot know statically.</summary>
public sealed class CustomResult : IResult
{
    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext) => Task.CompletedTask;
}

/// <summary>Actions returning typed <see cref="IResult"/> values.</summary>
[ApiController]
[Route("typed-results")]
public class TypedResultsController : ControllerBase
{
    private static readonly ResultItem Item = new() { ItemName = "item" };

    /// <summary>Union of results.</summary>
    [HttpGet("union")]
    public Results<Ok<ResultItem>, NotFound, BadRequest<ProblemDetails>> Union() => TypedResults.Ok(Item);

    /// <summary>Union of results inside a task.</summary>
    [HttpGet("union-task")]
    public Task<Results<Ok<ResultItem>, NotFound>> UnionInTask() =>
        Task.FromResult<Results<Ok<ResultItem>, NotFound>>(TypedResults.Ok(Item));

    /// <summary>A body with 200.</summary>
    [HttpGet("ok")]
    public Ok<ResultItem> OkItem() => TypedResults.Ok(Item);

    /// <summary>A body with 201.</summary>
    [HttpPost("created")]
    public Created<ResultItem> CreatedItem() => TypedResults.Created("/typed-results/ok", Item);

    /// <summary>No body.</summary>
    [HttpDelete("no-content")]
    public NoContent Deleted() => TypedResults.NoContent();

    /// <summary>Unauthorized without a body.</summary>
    [HttpGet("unauthorized")]
    public UnauthorizedHttpResult Denied() => TypedResults.Unauthorized();

    /// <summary>Validation problem.</summary>
    [HttpPost("validation")]
    public ValidationProblem Invalid() => TypedResults.ValidationProblem(new Dictionary<string, string[]>());

    /// <summary>An explicit declaration wins over the inferred response for the same code.</summary>
    [HttpGet("explicit")]
    [ProducesResponseType(typeof(ResultError), StatusCodes.Status404NotFound)]
    public Results<Ok<ResultItem>, NotFound<ResultItem>> Explicit() => TypedResults.Ok(Item);

    /// <summary>An untyped result.</summary>
    [HttpGet("untyped")]
    public IResult Untyped() => Results.Ok(Item);

    /// <summary>A user-defined result.</summary>
    [HttpGet("custom")]
    public CustomResult Custom() => new();

    /// <summary>A JSON result whose status is set at run time.</summary>
    [HttpGet("json")]
    public JsonHttpResult<ResultItem> Json() => TypedResults.Json(Item);

    /// <summary>An unknown status first, then a known one with a body.</summary>
    [HttpPost("unknown-first")]
    public Results<JsonHttpResult<ResultItem>, Created<ResultItem>> UnknownFirst() => TypedResults.Created("/x", Item);

    /// <summary>A known status with a body first, then an unknown one.</summary>
    [HttpGet("unknown-last")]
    public Results<Ok<ResultItem>, ProblemHttpResult> UnknownLast() => TypedResults.Ok(Item);

    /// <summary>A status-only result and an unknown one.</summary>
    [HttpGet("status-only-and-unknown")]
    public Results<NotFound, JsonHttpResult<ResultItem>> StatusOnlyAndUnknown() => TypedResults.NotFound();

    /// <summary>An untyped result with a declared response.</summary>
    [HttpGet("untyped-declared")]
    [ProducesResponseType(typeof(ResultItem), StatusCodes.Status200OK)]
    public IResult UntypedDeclared() => Results.Ok(Item);
}
