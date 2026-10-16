using System.Text.Json;

namespace DotNetOpenApiExtract.Core.Loading;

/// <summary>
/// The assemblies an application builds itself: the application and its project references, as its
/// <c>*.deps.json</c> lists them (libraries of type <c>project</c>). Types of every other assembly —
/// NuGet packages, the shared framework — come from code the application's author does not write.
/// </summary>
internal static class ProjectAssemblies
{
    /// <summary>
    /// The simple names of the project assemblies of the application at <paramref name="assemblyPath"/>,
    /// read from the <c>*.deps.json</c> beside it; the application itself is always included.
    /// <see langword="null"/> when there is no such file or it cannot be read: then which assemblies
    /// are the project's is unknown.
    /// </summary>
    public static IReadOnlySet<string>? Read(string assemblyPath)
    {
        var depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
        if (!File.Exists(depsPath)) return null;

        try
        {
            using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
            var root = deps.RootElement;
            if (!root.TryGetProperty("libraries", out var libraries) || !root.TryGetProperty("targets", out var targets))
                return null;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileNameWithoutExtension(assemblyPath) };
            foreach (var target in targets.EnumerateObject())
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!libraries.TryGetProperty(library.Name, out var info)
                    || !info.TryGetProperty("type", out var type)
                    || type.GetString() != "project")
                    continue;
                if (!library.Value.TryGetProperty("runtime", out var runtime)) continue;
                foreach (var asset in runtime.EnumerateObject())
                    names.Add(Path.GetFileNameWithoutExtension(asset.Name));
            }

            return names;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
