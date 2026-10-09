using Microsoft.OpenApi;

namespace DotNetOpenApiExtract.Core.Versioning;

/// <summary>Where an operation of a given HTTP method is written in a Path Item of a given version.</summary>
internal enum OperationSlot
{
    /// <summary>The method's own field (<c>get</c>, <c>trace</c>, <c>query</c> in 3.2, …).</summary>
    OwnField,

    /// <summary>3.2 <c>additionalOperations.&lt;M&gt;</c>: a method without its own field.</summary>
    AdditionalOperations,

    /// <summary>3.0/3.1 <c>x-oai-additionalOperations.&lt;M&gt;</c>, as Microsoft.OpenApi writes it.</summary>
    ExtensionAdditionalOperations,
}

/// <summary>
/// The Path Item fields per version: 3.0/3.1 have get, put, post, delete, options, head, patch and
/// trace; 3.2 adds query. Any other method goes to <c>additionalOperations</c> (3.2) or, for 3.0/3.1,
/// to the <c>x-oai-additionalOperations</c> extension the serializer writes instead.
/// </summary>
internal static class OperationPlacement
{
    /// <summary>The extension Microsoft.OpenApi writes for 3.0/3.1 instead of <c>additionalOperations</c>.</summary>
    public const string ExtensionName = "x-oai-additionalOperations";

    private static readonly HashSet<string> Fields30 = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "PUT", "POST", "DELETE", "OPTIONS", "HEAD", "PATCH", "TRACE",
    };

    /// <summary>Standard HTTP methods (as in <c>HttpMethods.*</c>), written in upper case whatever the literal.</summary>
    private static readonly HashSet<string> StandardMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE", "CONNECT", "QUERY",
    };

    /// <summary>
    /// The method as declared and keyed: standard methods upper case, any other method keeps the
    /// capitalization of the literal (what goes into the request).
    /// </summary>
    public static string NormalizeMethod(string method) =>
        StandardMethods.Contains(method) ? method.ToUpperInvariant() : method;

    public static OperationSlot SlotOf(string method, OpenApiSpecVersion version)
    {
        if (Fields30.Contains(method))
            return OperationSlot.OwnField;

        if (version == OpenApiSpecVersion.OpenApi3_2)
            return string.Equals(method, "QUERY", StringComparison.OrdinalIgnoreCase)
                ? OperationSlot.OwnField
                : OperationSlot.AdditionalOperations;

        return OperationSlot.ExtensionAdditionalOperations;
    }
}
