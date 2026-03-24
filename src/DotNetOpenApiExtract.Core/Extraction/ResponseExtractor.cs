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
}

/// <summary>
/// Extracts HTTP response information from controller action methods.
/// Inspects <c>[ProducesResponseType]</c>, <c>[SwaggerResponse]</c>, and
/// <c>[ProducesDefaultResponseType]</c> attributes via reflection-only metadata,
/// and falls back to return-type inference when no attributes are present.
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
    /// When no response attributes are present the list is inferred from
    /// the method's return type.
    /// </returns>
    public static IReadOnlyList<ResponseInfo> ExtractResponses(ActionInfo action)
    {
        var method = action.Method;
        var controller = action.Controller;

        // Resolve default content types from [Produces] on the method, then the controller.
        var defaultContentTypes = ResolveProducesContentTypes(method, controller.Type);

        var responses = new List<ResponseInfo>();

        // --- [SwaggerResponse] (highest priority) ---
        foreach (var attr in AttributeHelper.GetAttributes(method, AttributeHelper.Names.SwaggerResponse))
        {
            var response = ParseSwaggerResponse(attr, defaultContentTypes);
            if (response != null)
                responses.Add(response);
        }

        // --- [ProducesResponseType] (all overloads, including generic form) ---
        foreach (var attr in AttributeHelper.GetAttributes(method, AttributeHelper.Names.ProducesResponseType))
        {
            var response = ParseProducesResponseType(attr, defaultContentTypes);
            if (response != null)
                responses.Add(response);
        }

        // --- [ProducesDefaultResponseType] / [ProducesDefaultResponseType(typeof(T))] ---
        foreach (var attr in AttributeHelper.GetAttributes(method, AttributeHelper.Names.ProducesDefaultResponseType))
        {
            var response = ParseProducesDefaultResponseType(attr, defaultContentTypes);
            if (response != null)
                responses.Add(response);
        }

        // If we found any explicit declarations, return them (de-duplicated by status code,
        // keeping first-seen which corresponds to priority order above).
        if (responses.Count > 0)
            return DeduplicateByStatusCode(responses);

        // --- Fallback: infer from return type ---
        return InferFromReturnType(method, defaultContentTypes);
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
        IReadOnlyList<string> defaultContentTypes)
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

        return new ResponseInfo
        {
            StatusCode = statusCode.Value,
            Description = description,
            BodyType = bodyType,
            ContentTypes = defaultContentTypes,
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
        IReadOnlyList<string> defaultContentTypes)
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
        };
    }

    /// <summary>
    /// Parses a <c>[ProducesDefaultResponseType]</c> or
    /// <c>[ProducesDefaultResponseType(typeof(T))]</c> attribute.
    /// The status code is set to <see cref="DefaultStatusCode"/> (-1).
    /// </summary>
    private static ResponseInfo? ParseProducesDefaultResponseType(
        CustomAttributeData attr,
        IReadOnlyList<string> defaultContentTypes)
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
        };
    }

    // -------------------------------------------------------------------------
    // Return-type inference
    // -------------------------------------------------------------------------

    /// <summary>
    /// Infers response info from the method's declared return type when no
    /// explicit response attributes are present.
    /// </summary>
    private static IReadOnlyList<ResponseInfo> InferFromReturnType(
        MethodInfo method,
        IReadOnlyList<string> defaultContentTypes)
    {
        var returnType = method.ReturnType;
        var unwrapped = UnwrapReturnType(returnType);

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
    /// </summary>
    private static IReadOnlyList<string> ResolveProducesContentTypes(MethodInfo method, Type controllerType)
    {
        // Method-level [Produces] takes precedence.
        var methodAttr = AttributeHelper.GetAttribute(method, AttributeHelper.Names.Produces);
        if (methodAttr != null)
        {
            var ct = ParseProducesContentTypes(methodAttr);
            if (ct.Count > 0)
                return ct;
        }

        // Controller-level [Produces].
        var controllerAttr = AttributeHelper.GetAttribute(controllerType, AttributeHelper.Names.Produces);
        if (controllerAttr != null)
        {
            var ct = ParseProducesContentTypes(controllerAttr);
            if (ct.Count > 0)
                return ct;
        }

        return ["application/json"];
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
    // Deduplication
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a new list with only the first <see cref="ResponseInfo"/> for each
    /// distinct status code, preserving insertion order (i.e. priority order).
    /// When both [SwaggerResponse] and [ProducesResponseType] declare the same status code,
    /// the first-seen entry wins silently (SwaggerResponse has higher priority).
    /// </summary>
    private static IReadOnlyList<ResponseInfo> DeduplicateByStatusCode(List<ResponseInfo> responses)
    {
        var seen = new HashSet<int>();
        var result = new List<ResponseInfo>(responses.Count);
        foreach (var r in responses)
        {
            if (seen.Add(r.StatusCode))
                result.Add(r);
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
