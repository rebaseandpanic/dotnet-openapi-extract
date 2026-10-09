namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// The closed rule over ASP.NET Core typed results (<c>Microsoft.AspNetCore.Http.HttpResults</c>):
/// which status code and which body a result type writes, read from its metadata name only.
/// </summary>
internal static class TypedResults
{
    private const string ResultInterface = "Microsoft.AspNetCore.Http.IResult";
    private const string ValueResultInterface = "Microsoft.AspNetCore.Http.IValueHttpResult`1";
    private const string Namespace = "Microsoft.AspNetCore.Http.HttpResults.";
    private const string ServerSentEventsResult = Namespace + "ServerSentEventsResult`1";

    /// <summary>
    /// Framework results whose status code is fixed by the type. The status of every other result
    /// (<c>JsonHttpResult&lt;T&gt;</c>, <c>StatusCodeHttpResult</c>, <c>ProblemHttpResult</c>, redirects,
    /// challenges, a user-defined <c>IResult</c>, …) is not statically known.
    /// </summary>
    private static readonly Dictionary<string, int> StatusByType = new(StringComparer.Ordinal)
    {
        [Namespace + "Ok"]                     = 200,
        [Namespace + "Ok`1"]                   = 200,
        [Namespace + "Created"]                = 201,
        [Namespace + "Created`1"]              = 201,
        [Namespace + "CreatedAtRoute"]         = 201,
        [Namespace + "CreatedAtRoute`1"]       = 201,
        [Namespace + "Accepted"]               = 202,
        [Namespace + "Accepted`1"]             = 202,
        [Namespace + "AcceptedAtRoute"]        = 202,
        [Namespace + "AcceptedAtRoute`1"]      = 202,
        [Namespace + "NoContent"]              = 204,
        [Namespace + "BadRequest"]             = 400,
        [Namespace + "BadRequest`1"]           = 400,
        [Namespace + "ValidationProblem"]      = 400,
        [Namespace + "UnauthorizedHttpResult"] = 401,
        [Namespace + "NotFound"]               = 404,
        [Namespace + "NotFound`1"]             = 404,
        [Namespace + "Conflict"]               = 409,
        [Namespace + "Conflict`1"]             = 409,
        [Namespace + "UnprocessableEntity"]    = 422,
        [Namespace + "UnprocessableEntity`1"]  = 422,
        [Namespace + "InternalServerError"]    = 500,
        [Namespace + "InternalServerError`1"]  = 500,
    };

    /// <summary>Results whose body is a problem document.</summary>
    private static readonly HashSet<string> ProblemResults = new(StringComparer.Ordinal)
    {
        Namespace + "ValidationProblem",
    };

    /// <summary>Whether <paramref name="type"/> is <c>IResult</c> or implements it.</summary>
    public static bool IsResult(Type type) =>
        type.FullName == ResultInterface || type.GetInterfaces().Any(i => i.FullName == ResultInterface);

    /// <summary>
    /// The responses <paramref name="resultType"/> writes: one per variant of <c>Results&lt;…&gt;</c>, or
    /// the one response of a result with a known status. <see langword="null"/> when a status is not
    /// statically known.
    /// </summary>
    public static IReadOnlyList<ResponseInfo>? Responses(Type resultType)
    {
        var responses = new List<ResponseInfo>();
        return Collect(resultType, responses) ? responses : null;
    }

    private static bool Collect(Type type, List<ResponseInfo> responses)
    {
        var name = DefinitionName(type);

        if (name.StartsWith(Namespace + "Results`", StringComparison.Ordinal))
        {
            foreach (var variant in type.GetGenericArguments())
            {
                if (!Collect(variant, responses))
                    return false;
            }
            return true;
        }

        if (name == ServerSentEventsResult)
        {
            // Writes its own text/event-stream; the builder describes the events.
            Add(responses, new ResponseInfo { StatusCode = 200, BodyType = type, ContentTypes = ["text/event-stream"] });
            return true;
        }

        if (!StatusByType.TryGetValue(name, out var status))
            return false;

        var bodyType = ValueType(type);
        Add(responses, new ResponseInfo
        {
            StatusCode         = status,
            BodyType           = bodyType,
            ContentTypes       = bodyType == null ? [] : ProblemResults.Contains(name) ? ["application/problem+json"] : ["application/json"],
            BodyFromHttpResult = bodyType != null,
        });
        return true;
    }

    /// <summary>The first variant of a status code wins, as ASP.NET Core metadata does.</summary>
    private static void Add(List<ResponseInfo> responses, ResponseInfo response)
    {
        if (responses.All(r => r.StatusCode != response.StatusCode))
            responses.Add(response);
    }

    /// <summary><c>T</c> of the <c>IValueHttpResult&lt;T&gt;</c> the type implements, if any.</summary>
    private static Type? ValueType(Type type)
    {
        foreach (var implemented in type.GetInterfaces())
        {
            if (implemented.IsGenericType && implemented.GetGenericTypeDefinition().FullName == ValueResultInterface)
                return implemented.GetGenericArguments()[0];
        }

        return null;
    }

    private static string DefinitionName(Type type) =>
        (type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName) ?? type.Name;
}
