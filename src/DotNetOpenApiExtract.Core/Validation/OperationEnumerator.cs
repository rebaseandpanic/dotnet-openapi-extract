using Microsoft.OpenApi;
using DotNetOpenApiExtract.Core.Versioning;

namespace DotNetOpenApiExtract.Core.Validation;

/// <summary>Which top-level map an operation belongs to.</summary>
internal enum OperationRoot
{
    /// <summary><c>paths</c>: the key is a path template.</summary>
    Paths,

    /// <summary><c>webhooks</c>: the key is the webhook name.</summary>
    Webhooks,
}

/// <summary>One operation of a document, with where it is written in the output.</summary>
/// <param name="Root">The map holding the Path Item.</param>
/// <param name="Name">The path (for <see cref="OperationRoot.Paths"/>) or the webhook name.</param>
/// <param name="Method">The operation's HTTP method.</param>
/// <param name="Operation">The operation.</param>
/// <param name="PathItem">The Path Item holding it.</param>
/// <param name="Pointer">JSON pointer of the operation in the output of the validated version.</param>
internal sealed record OperationEntry(
    OperationRoot Root,
    string Name,
    HttpMethod Method,
    OpenApiOperation Operation,
    OpenApiPathItem PathItem,
    string Pointer)
{
    /// <summary>The method as written in messages and keys: standard methods upper case, others as declared.</summary>
    public string MethodName => OperationPlacement.NormalizeMethod(Method.Method);

    /// <summary>
    /// <c>METHOD /path</c> for an operation in <c>paths</c> (the key of build-time action bindings);
    /// <c>METHOD webhook-name</c> for a webhook operation.
    /// </summary>
    public string Key => $"{MethodName} {Name}";
}

/// <summary>
/// Every operation of a document, whatever its method and wherever it lives: <c>paths</c> and
/// <c>webhooks</c>, standard fields, 3.2 <c>query</c> and <c>additionalOperations</c>, and what
/// 3.0/3.1 hold under <c>x-oai-additionalOperations</c>. Paths come first in key order, then
/// webhooks in name order; methods in name order. Pointers follow the output of
/// <paramref name="version"/> (3.0 when unknown).
/// </summary>
internal static class OperationEnumerator
{
    public static IEnumerable<OperationEntry> Enumerate(OpenApiDocument document, OpenApiSpecVersion? version)
    {
        var target = version ?? OpenApiSpecVersion.OpenApi3_0;

        foreach (var entry in EnumerateRoot(OperationRoot.Paths, document.Paths, target))
            yield return entry;

        foreach (var entry in EnumerateRoot(OperationRoot.Webhooks, document.Webhooks, target))
            yield return entry;
    }

    private static IEnumerable<OperationEntry> EnumerateRoot(
        OperationRoot root,
        IEnumerable<KeyValuePair<string, IOpenApiPathItem>>? items,
        OpenApiSpecVersion version)
    {
        if (items == null)
            yield break;

        foreach (var (name, pathItemInterface) in items.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (pathItemInterface is not OpenApiPathItem { Operations: not null } pathItem)
                continue;

            foreach (var (method, operation) in pathItem.Operations.OrderBy(kv => kv.Key.Method, StringComparer.Ordinal))
            {
                var pointer = JsonPointerHelper.ForOperation(
                    root == OperationRoot.Paths ? "paths" : "webhooks", name, method.Method, version);
                yield return new OperationEntry(root, name, method, operation, pathItem, pointer);
            }
        }
    }
}
