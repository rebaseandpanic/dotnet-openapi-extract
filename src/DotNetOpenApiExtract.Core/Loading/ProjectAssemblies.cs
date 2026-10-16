using System.Text.Json;

namespace DotNetOpenApiExtract.Core.Loading;

/// <summary>
/// The origin of an application's assemblies, as its <c>*.deps.json</c> lists them: the assemblies it
/// builds itself — the application and its project references (libraries of type <c>project</c>) — and
/// those of its NuGet packages (type <c>package</c>). Types of every assembly but the project's — packages,
/// the shared framework, a DLL referenced directly (type <c>reference</c>) — come from code the
/// application's author does not write.
/// </summary>
/// <param name="Project">Simple names of the project assemblies, the application included.</param>
/// <param name="Package">Simple names of the runtime assemblies of NuGet packages.</param>
internal sealed record ProjectAssemblies(IReadOnlySet<string> Project, IReadOnlySet<string> Package)
{
    /// <summary>
    /// The assemblies of the application at <paramref name="assemblyPath"/>, read from the
    /// <c>*.deps.json</c> beside it; the application itself is always a project assembly.
    /// <see langword="null"/> when there is no such file or it cannot be read: then which assemblies
    /// are the project's is unknown.
    /// </summary>
    public static ProjectAssemblies? Read(string assemblyPath)
    {
        var depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
        if (!File.Exists(depsPath)) return null;

        try
        {
            using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
            var root = deps.RootElement;
            if (!root.TryGetProperty("libraries", out var libraries) || !root.TryGetProperty("targets", out var targets))
                return null;

            var project = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileNameWithoutExtension(assemblyPath) };
            var package = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets.EnumerateObject())
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!libraries.TryGetProperty(library.Name, out var info) || !info.TryGetProperty("type", out var type))
                    continue;
                var names = type.GetString() switch
                {
                    "project" => project,
                    "package" => package,
                    _ => null,
                };
                if (names == null || !library.Value.TryGetProperty("runtime", out var runtime)) continue;
                foreach (var asset in runtime.EnumerateObject())
                    names.Add(Path.GetFileNameWithoutExtension(asset.Name));
            }

            package.ExceptWith(project);
            return new ProjectAssemblies(project, package);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
