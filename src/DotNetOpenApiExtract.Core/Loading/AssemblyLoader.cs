using System.Reflection;
using System.Runtime.InteropServices;

namespace DotNetOpenApiExtract.Core.Loading;

/// <summary>
/// Loads .NET assemblies via MetadataLoadContext for static inspection
/// without executing any code.
/// </summary>
public sealed class AssemblyLoader : IDisposable
{
    private readonly MetadataLoadContext _context;
    private readonly Assembly _assembly;

    public Assembly Assembly => _assembly;

    public AssemblyLoader(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}", assemblyPath);

        assemblyPath = Path.GetFullPath(assemblyPath);
        var assemblyDir = Path.GetDirectoryName(assemblyPath)!;

        // Collect reference assemblies: app output dir + runtime dir
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // App assemblies (same directory as target DLL)
        foreach (var dll in Directory.GetFiles(assemblyDir, "*.dll"))
            paths.Add(dll);

        // Runtime assemblies (System.Runtime.dll, System.Collections.dll, etc.)
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
            paths.Add(dll);

        // Also check for ASP.NET Core shared framework assemblies
        // They live in shared/Microsoft.AspNetCore.App/{version}/
        var aspNetCoreDir = FindAspNetCoreDirectory(runtimeDir);
        if (aspNetCoreDir != null)
        {
            foreach (var dll in Directory.GetFiles(aspNetCoreDir, "*.dll"))
                paths.Add(dll);
        }

        var resolver = new PathAssemblyResolver(paths);
        _context = new MetadataLoadContext(resolver);
        _assembly = _context.LoadFromAssemblyPath(assemblyPath);
    }

    /// <summary>
    /// Resolves a type by full name from any loaded assembly in the context.
    /// Returns null if not found.
    /// </summary>
    public Type? FindType(string fullName)
    {
        // First try the main assembly
        var type = _assembly.GetType(fullName);
        if (type != null) return type;

        // Then try referenced assemblies
        foreach (var assemblyName in _assembly.GetReferencedAssemblies())
        {
            try
            {
                var refAssembly = _context.LoadFromAssemblyName(assemblyName);
                type = refAssembly.GetType(fullName);
                if (type != null) return type;
            }
            catch (Exception ex) when (ex is FileNotFoundException
                                        or BadImageFormatException
                                        or FileLoadException)
            {
                // Expected: assembly not resolvable in this context
            }
        }

        return null;
    }

    private static string? FindAspNetCoreDirectory(string runtimeDir)
    {
        // runtimeDir is like: /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.0/
        // We want:            /usr/share/dotnet/shared/Microsoft.AspNetCore.App/10.0.0/
        var dirInfo = new DirectoryInfo(runtimeDir);
        if (dirInfo.Parent?.Parent == null) return null;

        var sharedDir = dirInfo.Parent.Parent.FullName; // /usr/share/dotnet/shared/
        var version = dirInfo.Name; // 10.0.0

        var aspNetDir = Path.Combine(sharedDir, "Microsoft.AspNetCore.App", version);
        if (Directory.Exists(aspNetDir)) return aspNetDir;

        // Try to find closest version
        var aspNetBase = Path.Combine(sharedDir, "Microsoft.AspNetCore.App");
        if (!Directory.Exists(aspNetBase)) return null;

        return Directory.GetDirectories(aspNetBase)
            .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0))
            .FirstOrDefault();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
