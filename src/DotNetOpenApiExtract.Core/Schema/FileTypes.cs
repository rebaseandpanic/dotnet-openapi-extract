namespace DotNetOpenApiExtract.Core.Schema;

/// <summary>
/// Framework types that carry raw file content rather than JSON: MVC <c>FileResult</c> and its
/// derived types, <c>IFileHttpResult</c> results, <c>Stream</c> and <c>IFormFile</c>. Recognized by
/// metadata name, so the check works for types loaded through <c>MetadataLoadContext</c>.
/// </summary>
internal static class FileTypes
{
    private static readonly HashSet<string> BaseTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Mvc.FileResult",
        "System.IO.Stream",
    };

    private static readonly HashSet<string> Interfaces = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Http.IFileHttpResult",
        "Microsoft.AspNetCore.Http.IFormFile",
    };

    /// <summary>The media type of a file response when nothing declares one.</summary>
    public const string DefaultResponseMediaType = "application/octet-stream";

    /// <summary>Whether <paramref name="type"/> is one of the file types (itself or by inheritance).</summary>
    public static bool IsFile(Type type)
    {
        if (type.FullName is { } name && Interfaces.Contains(name))
            return true;

        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.FullName is { } baseName && BaseTypes.Contains(baseName))
                return true;
        }

        return type.GetInterfaces().Any(i => i.FullName is { } interfaceName && Interfaces.Contains(interfaceName));
    }
}
