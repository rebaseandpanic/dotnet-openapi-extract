using System.Reflection;
using DotNetOpenApiExtract.Core.Discovery;
using DotNetOpenApiExtract.Core.Loading;

namespace DotNetOpenApiExtract.Core.Extraction;

/// <summary>
/// Extracted response information from a controller action method.
/// </summary>
public sealed class ResponseInfo
{
    /// <summary>HTTP status code (e.g. 200, 201, 400, 422).
    /// Use <see cref="ResponseExtractor.DefaultStatusCode"/> to represent the OpenAPI "default" response.
    /// </summary>
    public required int StatusCode { get; init; }

    /// <summary>Response body type, or null if no body.</summary>
    public Type? BodyType { get; init; }

    /// <summary>Description of the response.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Content types for this response. Defaults to ["application/json"].
    /// Extractors override this explicitly — e.g. void/204 responses use an empty list.
    /// </summary>
    public IReadOnlyList<string> ContentTypes { get; init; } = ["application/json"];

    /// <summary>
    /// True when content types were explicitly set via <c>[Produces]</c> on the action or controller,
    /// rather than falling back to the default <c>["application/json"]</c>.
    /// Used to emit a <c>Content</c> section even when <see cref="BodyType"/> is null —
    /// for example, SSE endpoints that declare <c>[Produces("text/event-stream")]</c> with no body.
    /// </summary>
    public bool ContentTypesExplicit { get; init; }

    /// <summary>
    /// True when the body is written by a typed <c>IResult</c> (<c>Ok&lt;T&gt;</c>, <c>Results&lt;…&gt;</c>
    /// variants, …): ASP.NET Core serializes it with the HTTP JSON options
    /// (<c>ConfigureHttpJsonOptions</c>), not the MVC ones, and ignores <c>[Produces]</c>.
    /// </summary>
    public bool BodyFromHttpResult { get; init; }

    /// <summary>
    /// The action returns an <c>IResult</c> whose status code is not statically known (untyped
    /// <c>IResult</c>, a user-defined result, …): the response is written without a schema.
    /// </summary>
    internal bool UnknownResultStatus { get; init; }
}

/// <summary>
/// Extracts HTTP response information from controller action methods.
/// Inspects <c>[ProducesResponseType]</c>, <c>[SwaggerResponse]</c>,
/// <c>[ProducesDefaultResponseType]</c> and <c>[Produces]</c> on the action and its controller via
/// reflection-only metadata, and falls back to return-type inference when nothing declares a response.
/// </summary>
public static class ResponseExtractor
{
    /// <summary>
    /// Sentinel status code representing the OpenAPI "default" response.
    /// Used when a <c>[ProducesDefaultResponseType]</c> attribute is present.
    /// </summary>
    public const int DefaultStatusCode = -1;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts all response descriptions declared on <paramref name="action"/>.
    /// </summary>
    /// <param name="action">The action to inspect.</param>
    /// <returns>
    /// A non-empty, ordered list of <see cref="ResponseInfo"/> instances.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Responses are declared by <c>[SwaggerResponse]</c>, <c>[ProducesResponseType]</c> (all
    /// overloads, including <c>[ProducesResponseType&lt;T&gt;]</c>) and
    /// <c>[ProducesDefaultResponseType]</c> on the action and on its controller. For one status code
    /// the action's declaration wins over the controller's, field by field: a field the action leaves
    /// open (body type, description, content types) is taken from the controller.
    /// </para>
    /// <para>
    /// The <b>effective response type</b> of a status code is, in priority order: the type declared
    /// for that code (action, then controller); for the 200 response, the type of
    /// <c>[Produces(typeof(T))]</c> / <c>[Produces&lt;T&gt;]</c> (action, then controller); for the
    /// 200 response, the return type after <c>Task&lt;&gt;</c>, <c>ValueTask&lt;&gt;</c> and
    /// <c>ActionResult&lt;&gt;</c> are removed. As in ASP.NET Core ApiExplorer, <c>[Produces]</c>
    /// with a type declares a 200 response; the return type yields a response only when nothing
    /// else declares one (<c>void</c> / <c>Task</c> → 204, <c>IActionResult</c> → 200 without a body).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ResponseInfo> ExtractResponses(ActionInfo action)
    {
        var (defaultContentTypes, contentTypesExplicit) = ResolveProducesContentTypes(action.Method, action.Controller.Type);
        return ExtractResponsesCore(action, defaultContentTypes, contentTypesExplicit)
            .Select(r => WithFileMediaType(r, defaultContentTypes, contentTypesExplicit))
            .ToList();
    }

    /// <summary>
    /// A file body is raw bytes: unless its response declares media types of its own or
    /// <c>[Produces]</c> declares them, it is written as <c>application/octet-stream</c>, never JSON.
    /// </summary>
    private static ResponseInfo WithFileMediaType(
        ResponseInfo response, IReadOnlyList<string> defaultContentTypes, bool contentTypesExplicit)
    {
        if (response.BodyType == null || !Schema.FileTypes.IsFile(response.BodyType))
            return response;

        var ownTypes = response.ContentTypes.Count > 0 && !ReferenceEquals(response.ContentTypes, defaultContentTypes);
        if (ownTypes)
            return response;

        return new ResponseInfo
        {
            StatusCode           = response.StatusCode,
            BodyType             = response.BodyType,
            Description          = response.Description,
            ContentTypes         = contentTypesExplicit ? defaultContentTypes : [Schema.FileTypes.DefaultResponseMediaType],
            ContentTypesExplicit = contentTypesExplicit,
            BodyFromHttpResult   = false,
        };
    }

    private static IReadOnlyList<ResponseInfo> ExtractResponsesCore(
        ActionInfo action, IReadOnlyList<string> defaultContentTypes, bool contentTypesExplicit)
    {
        var method = action.Method;
        var controllerType = action.Controller.Type;

        var declared = MergeLevels(
            CollectDeclared(method, defaultContentTypes, contentTypesExplicit),
            CollectDeclared(controllerType, defaultContentTypes, contentTypesExplicit));

        var producesType = ResolveProducesType(method) ?? ResolveProducesType(controllerType);
        var signatureType = UnwrapReturnType(method.ReturnType);
        var signatureBody = signatureType == null || IsVoidLike(signatureType) || IsNonGenericActionResult(signatureType)
            ? null
            : signatureType;

        // A typed IResult declares its own responses; explicit declarations win per status code.
        if (signatureType != null && TypedResults.IsResult(signatureType))
            return MergeWithResultResponses(declared.Select(d => d.Response).ToList(), signatureType);

        if (declared.Count == 0 && producesType == null)
            return InferFromReturnType(signatureType, defaultContentTypes, contentTypesExplicit);

        var responses = new List<ResponseInfo>(declared.Count + 1);
        var has200 = false;
        foreach (var entry in declared)
        {
            var response = entry.Response;
            if (response.StatusCode == 200)
            {
                has200 = true;
                if (response.BodyType == null)
                {
                    // A 200 declared without a type: [Produces(typeof(T))], then the return type.
                    var bodyType = producesType ?? signatureBody;
                    if (bodyType != null)
                        response = Copy(response, bodyType);
                }
            }
            responses.Add(response);
        }

        // [Produces(typeof(T))] declares the 200 response itself.
        if (!has200 && producesType != null)
        {
            responses.Add(new ResponseInfo
            {
                StatusCode = 200,
                BodyType = producesType,
                ContentTypes = defaultContentTypes,
                ContentTypesExplicit = contentTypesExplicit,
            });
        }

        return responses;
    }

    /// <summary>
    /// The declared responses (their bodies, too, are written by the result, in the HTTP context)
    /// followed by the responses the typed result <paramref name="resultType"/> writes, for the status
    /// codes not declared. A result whose status is not statically known
    /// adds a 200 response without a body, flagged so the caller can report it, unless something
    /// declares a response.
    /// </summary>
    private static IReadOnlyList<ResponseInfo> MergeWithResultResponses(List<ResponseInfo> declared, Type resultType)
    {
        // Whatever an IResult action declares is still written by the result, with the HTTP JSON options.
        var responses = declared
            .Select(r => r.BodyType == null || r.BodyFromHttpResult ? r : new ResponseInfo
            {
                StatusCode           = r.StatusCode,
                BodyType             = r.BodyType,
                Description          = r.Description,
                ContentTypes         = r.ContentTypes,
                ContentTypesExplicit = r.ContentTypesExplicit,
                BodyFromHttpResult   = true,
            })
            .ToList();
        var codes = declared.Select(r => r.StatusCode).ToHashSet();

        var inferred = TypedResults.Responses(resultType);
        if (inferred == null)
        {
            if (responses.Count == 0)
            {
                responses.Add(new ResponseInfo
                {
                    StatusCode          = 200,
                    ContentTypes        = [],
                    UnknownResultStatus = true,
                });
            }

            return responses;
        }

        foreach (var response in inferred)
        {
            if (codes.Add(response.StatusCode))
                responses.Add(response);
        }

        return responses;
    }

    /// <summary>A declared response and whether its content types came from its own attribute.</summary>
    private readonly record struct DeclaredResponse(ResponseInfo Response, bool OwnContentTypes);

    /// <summary>
    /// Responses declared on one level (action method or controller class), first declaration per
    /// status code wins: <c>[SwaggerResponse]</c>, then <c>[ProducesResponseType]</c>, then
    /// <c>[ProducesDefaultResponseType]</c>.
    /// </summary>
    private static List<DeclaredResponse> CollectDeclared(
        MemberInfo member, IReadOnlyList<string> defaultContentTypes, bool contentTypesExplicit)
    {
        var result = new List<DeclaredResponse>();
        var seen = new HashSet<int>();

        void Add(ResponseInfo? response)
        {
            if (response != null && seen.Add(response.StatusCode))
                result.Add(new DeclaredResponse(response, !ReferenceEquals(response.ContentTypes, defaultContentTypes)));
        }

        foreach (var attr in AttributeHelper.GetAttributes(member, AttributeHelper.Names.SwaggerResponse))
            Add(ParseSwaggerResponse(attr, defaultContentTypes, contentTypesExplicit));

        foreach (var attr in AttributeHelper.GetAttributesWithGenericForm(member, AttributeHelper.Names.ProducesResponseType))
            Add(ParseProducesResponseType(attr, defaultContentTypes, contentTypesExplicit));

        foreach (var attr in AttributeHelper.GetAttributes(member, AttributeHelper.Names.ProducesDefaultResponseType))
            Add(ParseProducesDefaultResponseType(attr, defaultContentTypes, contentTypesExplicit));

        return result;
    }

    /// <summary>
    /// Action responses in their order, then controller-only status codes in theirs. For a status
    /// code declared on both levels, every field the action leaves open comes from the controller.
    /// </summary>
    private static List<DeclaredResponse> MergeLevels(
        List<DeclaredResponse> action, List<DeclaredResponse> controller)
    {
        var byCode = controller.ToDictionary(c => c.Response.StatusCode);
        var result = new List<DeclaredResponse>(action.Count + controller.Count);

        foreach (var entry in action)
        {
            if (!byCode.Remove(entry.Response.StatusCode, out var fallback))
            {
                result.Add(entry);
                continue;
            }

            var own = entry.Response;
            var useControllerContentTypes = !entry.OwnContentTypes && fallback.OwnContentTypes;
            result.Add(new DeclaredResponse(new ResponseInfo
            {
                StatusCode = own.StatusCode,
                BodyType = own.BodyType ?? fallback.Response.BodyType,
                Description = own.Description ?? fallback.Response.Description,
                ContentTypes = useControllerContentTypes ? fallback.Response.ContentTypes : own.ContentTypes,
                ContentTypesExplicit = own.ContentTypesExplicit,
            }, entry.OwnContentTypes || fallback.OwnContentTypes));
        }

        foreach (var entry in controller)
        {
            if (byCode.ContainsKey(entry.Response.StatusCode))
                result.Add(entry);
        }

        return result;
    }

    private static ResponseInfo Copy(ResponseInfo response, Type bodyType) => new()
    {
        StatusCode = response.StatusCode,
        BodyType = bodyType,
        Description = response.Description,
        ContentTypes = response.ContentTypes,
        ContentTypesExplicit = response.ContentTypesExplicit,
    };

    /// <summary>
    /// The response type declared by <c>[Produces(typeof(T))]</c> or <c>[Produces&lt;T&gt;]</c> on
    /// <paramref name="member"/>, or <see langword="null"/>.
    /// </summary>
    private static Type? ResolveProducesType(MemberInfo member)
    {
        foreach (var attr in AttributeHelper.GetAttributesWithGenericForm(member, AttributeHelper.Names.Produces))
        {
            if (attr.AttributeType.IsGenericType)
                return attr.AttributeType.GetGenericArguments()[0];

            if (attr.ConstructorArguments.Count == 1 && IsTypeArgument(attr.ConstructorArguments[0])
                && attr.ConstructorArguments[0].Value is Type type)
                return type;
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Attribute parsers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a <c>[SwaggerResponse(statusCode, description, typeof(T))]</c> attribute.
    /// Constructor signature: (int statusCode, string? description, Type? type).
    /// </summary>
    private static ResponseInfo? ParseSwaggerResponse(
        CustomAttributeData attr,
        IReadOnlyList<string> defaultContentTypes,
        bool contentTypesExplicit)
    {
        // Arg 0: int statusCode
        if (attr.ConstructorArguments.Count < 1)
            return null;

        var statusCode = GetIntArg(attr, 0);
        if (statusCode is null)
            return null;

        // Arg 1 (optional): string description
        var description = attr.ConstructorArguments.Count >= 2
            ? attr.ConstructorArguments[1].Value as string
            : null;

        // Arg 2 (optional): Type
        Type? bodyType = null;
        if (attr.ConstructorArguments.Count >= 3)
            bodyType = attr.ConstructorArguments[2].Value as Type;

        // Arg 3 (optional): params string[] contentTypes — the response's own media types.
        var contentTypes = new List<string>();
        if (attr.ConstructorArguments.Count >= 4
            && attr.ConstructorArguments[3].Value is IReadOnlyCollection<CustomAttributeTypedArgument> declared)
        {
            foreach (var item in declared)
            {
                if (item.Value is string ct && !string.IsNullOrWhiteSpace(ct))
                    contentTypes.Add(ct);
            }
        }

        return new ResponseInfo
        {
            StatusCode = statusCode.Value,
            Description = description,
            BodyType = bodyType,
            ContentTypes = contentTypes.Count > 0 ? contentTypes : defaultContentTypes,
            ContentTypesExplicit = contentTypesExplicit || contentTypes.Count > 0,
        };
    }

    /// <summary>
    /// Parses a <c>[ProducesResponseType(...)]</c> attribute in all its overload forms.
    /// <para>Supported constructor signatures:</para>
    /// <list type="bullet">
    ///   <item><c>(int statusCode)</c></item>
    ///   <item><c>(Type type, int statusCode)</c></item>
    ///   <item><c>(Type type, int statusCode, string contentType, params string[] additionalContentTypes)</c></item>
    /// </list>
    /// The generic form <c>[ProducesResponseType&lt;T&gt;(statusCode)]</c> exposes T as the
    /// first generic argument of the attribute type itself.
    /// </summary>
    private static ResponseInfo? ParseProducesResponseType(
        CustomAttributeData attr,
        IReadOnlyList<string> defaultContentTypes,
        bool contentTypesExplicit)
    {
        var args = attr.ConstructorArguments;

        // Generic form: [ProducesResponseType<T>(statusCode)]
        // The generic type argument T is carried on the attribute type, not in ctor args.
        Type? genericBodyType = null;
        if (attr.AttributeType.IsGenericType)
        {
            var typeArgs = attr.AttributeType.GetGenericArguments();
            if (typeArgs.Length == 1)
                genericBodyType = typeArgs[0];
        }

        int? statusCode;
        Type? bodyType = genericBodyType; // may be overwritten below for non-generic form
        IReadOnlyList<string> contentTypes = defaultContentTypes;

        if (args.Count == 1)
        {
            // (int statusCode)
            statusCode = GetIntArg(attr, 0);
            // bodyType stays as genericBodyType (null for non-generic)
        }
        else if (args.Count == 2)
        {
            // (Type type, int statusCode)  — Type first, int second
            // Distinguish by ArgumentType to handle both orderings defensively.
            var first = args[0];
            var second = args[1];

            if (IsTypeArgument(first) && IsIntArgument(second))
            {
                bodyType = (first.Value as Type) ?? genericBodyType;
                statusCode = CastToInt(second.Value);
            }
            else if (IsIntArgument(first) && IsTypeArgument(second))
            {
                // Defensive: handle (int, Type) just in case
                statusCode = CastToInt(first.Value);
                bodyType = (second.Value as Type) ?? genericBodyType;
            }
            else
            {
                // Neither combination matched — skip
                return null;
            }
        }
        else if (args.Count >= 3)
        {
            // (Type type, int statusCode, string contentType, params string[] additionalContentTypes)
            if (!IsTypeArgument(args[0]) || !IsIntArgument(args[1]))
                return null;

            bodyType = (args[0].Value as Type) ?? genericBodyType;
            statusCode = CastToInt(args[1].Value);

            // Collect content types from the remaining constructor arguments.
            var ctList = new List<string>();

            // Arg index 2: string contentType
            if (args[2].Value is string ct0 && !string.IsNullOrWhiteSpace(ct0))
                ctList.Add(ct0);

            // Arg index 3+: params string[] additionalContentTypes
            // In MetadataLoadContext these may arrive as a single
            // IReadOnlyCollection<CustomAttributeTypedArgument> or as a plain string.
            if (args.Count > 3)
            {
                var additionalArg = args[3];
                if (additionalArg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> nested)
                {
                    foreach (var item in nested)
                    {
                        if (item.Value is string s && !string.IsNullOrWhiteSpace(s))
                            ctList.Add(s);
                    }
                }
                else if (additionalArg.Value is string s2 && !string.IsNullOrWhiteSpace(s2))
                {
                    ctList.Add(s2);
                }
            }

            contentTypes = ctList.Count > 0
                ? ctList.AsReadOnly()
                : defaultContentTypes;
        }
        else
        {
            return null;
        }

        if (statusCode is null)
            return null;

        // .NET 10+ named argument: Description
        var description = AttributeHelper.GetNamedArgument<string>(attr, "Description");

        return new ResponseInfo
        {
            StatusCode = statusCode.Value,
            BodyType = bodyType,
            Description = description,
            ContentTypes = contentTypes,
            ContentTypesExplicit = contentTypesExplicit,
        };
    }

    /// <summary>
    /// Parses a <c>[ProducesDefaultResponseType]</c> or
    /// <c>[ProducesDefaultResponseType(typeof(T))]</c> attribute.
    /// The status code is set to <see cref="DefaultStatusCode"/> (-1).
    /// </summary>
    private static ResponseInfo? ParseProducesDefaultResponseType(
        CustomAttributeData attr,
        IReadOnlyList<string> defaultContentTypes,
        bool contentTypesExplicit)
    {
        // Optional ctor arg: (Type type)
        Type? bodyType = null;
        if (attr.ConstructorArguments.Count >= 1 && IsTypeArgument(attr.ConstructorArguments[0]))
            bodyType = attr.ConstructorArguments[0].Value as Type;

        return new ResponseInfo
        {
            StatusCode = DefaultStatusCode,
            BodyType = bodyType,
            ContentTypes = defaultContentTypes,
            ContentTypesExplicit = contentTypesExplicit,
        };
    }

    // -------------------------------------------------------------------------
    // Return-type inference
    // -------------------------------------------------------------------------

    /// <summary>
    /// Infers response info from the method's unwrapped return type when nothing declares a
    /// response.
    /// </summary>
    private static IReadOnlyList<ResponseInfo> InferFromReturnType(
        Type? unwrapped,
        IReadOnlyList<string> defaultContentTypes,
        bool contentTypesExplicit)
    {
        // void → 204 No Content
        if (unwrapped == null || IsVoidLike(unwrapped))
        {
            return
            [
                new ResponseInfo
                {
                    StatusCode = 204,
                    ContentTypes = [],
                },
            ];
        }

        // ActionResult / IActionResult (non-generic) → 200 with no body type
        if (IsNonGenericActionResult(unwrapped))
        {
            return
            [
                new ResponseInfo
                {
                    StatusCode = 200,
                    ContentTypes = defaultContentTypes,
                    ContentTypesExplicit = contentTypesExplicit,
                },
            ];
        }

        // Any other concrete type (including ActionResult<T> already unwrapped to T) → 200 with body
        return
        [
            new ResponseInfo
            {
                StatusCode = 200,
                BodyType = unwrapped,
                ContentTypes = defaultContentTypes,
                ContentTypesExplicit = contentTypesExplicit,
            },
        ];
    }

    /// <summary>
    /// Recursively unwraps <c>Task&lt;T&gt;</c>, <c>ValueTask&lt;T&gt;</c>, and
    /// <c>ActionResult&lt;T&gt;</c> until a non-wrapping type is reached, then returns it.
    /// Returns <see langword="null"/> for <c>void</c>.
    /// </summary>
    internal static Type? UnwrapReturnType(Type type)
    {
        // void
        if (type.FullName == "System.Void")
            return null;

        if (!type.IsGenericType)
            return type;

        var def = type.GetGenericTypeDefinition();
        var defName = def.FullName ?? def.Name;

        // Task<T> / ValueTask<T> → unwrap to T
        if (defName is "System.Threading.Tasks.Task`1"
                    or "System.Threading.Tasks.ValueTask`1")
        {
            var inner = type.GetGenericArguments()[0];
            return UnwrapReturnType(inner);
        }

        // ActionResult<T> → unwrap to T
        if (defName == "Microsoft.AspNetCore.Mvc.ActionResult`1")
        {
            var inner = type.GetGenericArguments()[0];
            return UnwrapReturnType(inner);
        }

        // All other generic types (e.g. IEnumerable<T>, List<T>) are returned as-is.
        return type;
    }

    // -------------------------------------------------------------------------
    // Content-type resolution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the default content types by inspecting <c>[Produces]</c> on the
    /// method first, then on the controller class. Falls back to
    /// <c>["application/json"]</c> when neither is present.
    /// Returns the content-type list and a flag indicating whether they came from
    /// an explicit <c>[Produces]</c> attribute (true) or from the default fallback (false).
    /// </summary>
    private static (IReadOnlyList<string> ContentTypes, bool Explicit) ResolveProducesContentTypes(
        MethodInfo method, Type controllerType)
    {
        // Method-level [Produces] takes precedence.
        var methodAttr = AttributeHelper.GetAttribute(method, AttributeHelper.Names.Produces);
        if (methodAttr != null)
        {
            var ct = ParseProducesContentTypes(methodAttr);
            if (ct.Count > 0)
                return (ct, true);
        }

        // Controller-level [Produces].
        var controllerAttr = AttributeHelper.GetAttribute(controllerType, AttributeHelper.Names.Produces);
        if (controllerAttr != null)
        {
            var ct = ParseProducesContentTypes(controllerAttr);
            if (ct.Count > 0)
                return (ct, true);
        }

        return (["application/json"], false);
    }

    /// <summary>
    /// Extracts content-type strings from a <c>[Produces]</c> attribute.
    /// Constructor signature: <c>(string contentType, params string[] additionalContentTypes)</c>.
    /// </summary>
    private static IReadOnlyList<string> ParseProducesContentTypes(CustomAttributeData attr)
    {
        var result = new List<string>();

        // Arg 0: string contentType (required)
        if (attr.ConstructorArguments.Count >= 1 && attr.ConstructorArguments[0].Value is string primary
            && !string.IsNullOrWhiteSpace(primary))
        {
            result.Add(primary);
        }

        // Arg 1: params string[] additionalContentTypes
        if (attr.ConstructorArguments.Count >= 2)
        {
            var additionalArg = attr.ConstructorArguments[1];
            if (additionalArg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> nested)
            {
                foreach (var item in nested)
                {
                    if (item.Value is string s && !string.IsNullOrWhiteSpace(s))
                        result.Add(s);
                }
            }
            else if (additionalArg.Value is string s2 && !string.IsNullOrWhiteSpace(s2))
            {
                result.Add(s2);
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Type classification helpers
    // -------------------------------------------------------------------------

    /// <summary>Returns true when the argument's declared type is an integer primitive.</summary>
    private static bool IsIntArgument(CustomAttributeTypedArgument arg)
    {
        var fullName = arg.ArgumentType.FullName;
        return fullName is "System.Int32" or "System.Int64" or "System.Int16"
                        or "System.UInt32" or "System.UInt64" or "System.UInt16"
                        or "System.Byte" or "System.SByte";
    }

    /// <summary>Returns true when the argument's declared type is <c>System.Type</c>.</summary>
    private static bool IsTypeArgument(CustomAttributeTypedArgument arg)
        => arg.ArgumentType.FullName == "System.Type";

    /// <summary>Safely casts an attribute argument value to int.</summary>
    private static int? CastToInt(object? value)
    {
        return value switch
        {
            int i    => i,
            long l   => l is >= int.MinValue and <= int.MaxValue ? (int)l : null,
            short s  => s,
            uint u   => u <= int.MaxValue ? (int)u : null,
            ulong ul => ul <= int.MaxValue ? (int)ul : null,
            ushort us => us,
            byte b   => b,
            sbyte sb => sb,
            _        => null,
        };
    }

    /// <summary>
    /// Reads constructor argument at <paramref name="index"/> as <c>int</c>,
    /// returning null if missing or wrong type.
    /// </summary>
    private static int? GetIntArg(CustomAttributeData attr, int index)
    {
        if (index >= attr.ConstructorArguments.Count)
            return null;

        return CastToInt(attr.ConstructorArguments[index].Value);
    }

    /// <summary>
    /// Returns true for types that represent "no body":
    /// <c>void</c>, <c>Task</c> (non-generic), <c>ValueTask</c> (non-generic).
    /// </summary>
    private static bool IsVoidLike(Type type)
    {
        var fn = type.FullName ?? type.Name;
        return fn is "System.Void"
                  or "System.Threading.Tasks.Task"
                  or "System.Threading.Tasks.ValueTask";
    }

    /// <summary>
    /// Returns true for the non-generic <c>ActionResult</c> and <c>IActionResult</c> types.
    /// </summary>
    private static bool IsNonGenericActionResult(Type type)
    {
        var fn = type.FullName ?? type.Name;
        return fn is "Microsoft.AspNetCore.Mvc.ActionResult"
                  or "Microsoft.AspNetCore.Mvc.IActionResult";
    }
}
