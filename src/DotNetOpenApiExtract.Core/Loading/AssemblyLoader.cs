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

    /// <summary>Directories that were searched for DLLs, used for XML doc discovery.</summary>
    private readonly IReadOnlyList<string> _resolverDirectories;

    /// <summary>Cached runtime directory — used by both ctor and <see cref="GetXmlDocumentationFiles"/>.</summary>
    private readonly string _runtimeDir;

    /// <summary>
    /// Paths to ref-pack directories that were expected but not found on disk.
    /// The caller can use this to emit a single stderr warning.
    /// </summary>
    public IReadOnlyList<string> MissingRefPackHints { get; }

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
        _runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in Directory.GetFiles(_runtimeDir, "*.dll"))
            paths.Add(dll);

        // Also check for ASP.NET Core shared framework assemblies
        // They live in shared/Microsoft.AspNetCore.App/{version}/
        var aspNetCoreDir = FindAspNetCoreDirectory(_runtimeDir);
        if (aspNetCoreDir != null)
        {
            foreach (var dll in Directory.GetFiles(aspNetCoreDir, "*.dll"))
                paths.Add(dll);
        }

        var resolver = new PathAssemblyResolver(paths);
        _context = new MetadataLoadContext(resolver);
        _assembly = _context.LoadFromAssemblyPath(assemblyPath);

        // Track unique directories for XML discovery (use all dirs that contributed DLLs)
        var dirs = new List<string> { assemblyDir, _runtimeDir };
        if (aspNetCoreDir != null) dirs.Add(aspNetCoreDir);
        _resolverDirectories = dirs;

        // Discover ref-pack directories alongside the shared framework dirs
        MissingRefPackHints = FindMissingRefPackHints(_runtimeDir, aspNetCoreDir);
    }

    /// <summary>
    /// Returns the set of XML documentation files discovered across all resolver directories,
    /// including SDK ref-pack directories. Only returns XML files that have a sibling DLL
    /// (avoids picking up unrelated XMLs like NuGet nuspec-adjacent docs).
    /// Files are returned in priority order: assembly output dir first, then runtime dirs,
    /// then ref-pack dirs.
    /// </summary>
    public IReadOnlyList<string> GetXmlDocumentationFiles()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Phase 1: resolver directories (app output + shared framework dirs)
        foreach (var dir in _resolverDirectories)
        {
            if (!Directory.Exists(dir)) continue;
            CollectXmlFilesWithDllSibling(dir, result, seen);
        }

        // Phase 2: ref-pack directories (parallel to shared framework dirs).
        // Track count after Phase 1 so the caller can distinguish "framework XML found" from
        // "any XML found at all" — the project's own XML lives in Phase 1's assembly dir.
        _refPackXmlStartIndex = result.Count;

        var refPackDirs = FindRefPackDirectories(_runtimeDir);
        foreach (var dir in refPackDirs)
        {
            if (!Directory.Exists(dir)) continue;
            CollectXmlFilesWithDllSibling(dir, result, seen);
        }

        _refPackXmlCount = result.Count - _refPackXmlStartIndex;
        return result;
    }

    private int _refPackXmlStartIndex;
    private int _refPackXmlCount;

    /// <summary>
    /// Count of XML files contributed by ref-pack directories on the most recent call to
    /// <see cref="GetXmlDocumentationFiles"/>. Zero means no framework XML was discovered.
    /// </summary>
    public int RefPackXmlCount => _refPackXmlCount;

    /// <summary>
    /// Collects all *.xml files in <paramref name="directory"/> that have a sibling *.dll.
    /// Adds each path to <paramref name="result"/> and <paramref name="seen"/> if not yet seen.
    /// </summary>
    private static void CollectXmlFilesWithDllSibling(
        string directory,
        List<string> result,
        HashSet<string> seen)
    {
        foreach (var xmlFile in Directory.GetFiles(directory, "*.xml"))
        {
            var dllSibling = Path.ChangeExtension(xmlFile, ".dll");
            if (!File.Exists(dllSibling)) continue;
            if (seen.Add(xmlFile))
                result.Add(xmlFile);
        }
    }

    /// <summary>
    /// Returns the ref-pack directories that correspond to the given runtime directory.
    /// For example, given <c>/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.5/</c>,
    /// returns paths like:
    /// <c>/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.5/ref/net10.0/</c> and
    /// <c>/usr/share/dotnet/packs/Microsoft.AspNetCore.App.Ref/10.0.5/ref/net10.0/</c>.
    /// Missing directories are silently excluded from the result.
    /// </summary>
    private static IReadOnlyList<string> FindRefPackDirectories(string runtimeDir)
    {
        // runtimeDir: /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.5/
        var dirInfo = new DirectoryInfo(runtimeDir);
        if (dirInfo.Parent?.Parent == null) return [];

        var sharedDir = dirInfo.Parent.Parent.FullName; // /usr/share/dotnet/shared/
        var dotnetRoot = Path.GetDirectoryName(sharedDir); // /usr/share/dotnet
        if (dotnetRoot == null) return [];

        var packsRoot = Path.Combine(dotnetRoot, "packs"); // /usr/share/dotnet/packs
        if (!Directory.Exists(packsRoot)) return [];

        var runtimeVersion = dirInfo.Name; // e.g. "10.0.5"
        if (!Version.TryParse(runtimeVersion, out var parsedVersion)) return [];

        var result = new List<string>();

        // Look for both NETCore and AspNetCore ref packs
        var refPackNames = new[] { "Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref" };
        foreach (var packName in refPackNames)
        {
            var packBase = Path.Combine(packsRoot, packName);
            if (!Directory.Exists(packBase)) continue;

            var packDir = FindBestVersionedDirectory(packBase, parsedVersion);
            if (packDir == null) continue;

            // Ref packs have structure: packs/{PackName}/{version}/ref/net{major}.{minor}/
            var tfm = $"net{parsedVersion.Major}.{parsedVersion.Minor}";
            var refDir = Path.Combine(packDir, "ref", tfm);
            if (Directory.Exists(refDir))
                result.Add(refDir);
        }

        return result;
    }

    /// <summary>
    /// Collects ref-pack directory paths that were expected but not present on disk.
    /// The result is used to emit a single stderr warning in the caller.
    /// </summary>
    private static IReadOnlyList<string> FindMissingRefPackHints(
        string runtimeDir,
        string? aspNetCoreDir)
    {
        var dirInfo = new DirectoryInfo(runtimeDir);
        if (dirInfo.Parent?.Parent == null) return [];

        var sharedDir = dirInfo.Parent.Parent.FullName;
        var dotnetRoot = Path.GetDirectoryName(sharedDir);
        if (dotnetRoot == null) return [];

        var packsRoot = Path.Combine(dotnetRoot, "packs");
        var runtimeVersion = dirInfo.Name;
        if (!Version.TryParse(runtimeVersion, out var parsedVersion)) return [];

        var tfm = $"net{parsedVersion.Major}.{parsedVersion.Minor}";
        var missing = new List<string>();

        var refPackNames = new[] { "Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref" };
        foreach (var packName in refPackNames)
        {
            var packBase = Path.Combine(packsRoot, packName);
            if (!Directory.Exists(packBase))
            {
                missing.Add(Path.Combine(packsRoot, packName, runtimeVersion, "ref", tfm));
                continue;
            }

            var packDir = FindBestVersionedDirectory(packBase, parsedVersion);
            if (packDir == null)
            {
                missing.Add(Path.Combine(packBase, runtimeVersion, "ref", tfm));
                continue;
            }

            var refDir = Path.Combine(packDir, "ref", tfm);
            if (!Directory.Exists(refDir))
                missing.Add(refDir);
        }

        return missing;
    }

    /// <summary>
    /// Finds the best-matching versioned subdirectory under <paramref name="baseDir"/>
    /// for the given <paramref name="targetVersion"/>. Prefers exact match; falls back to
    /// nearest by Major.Minor.
    /// </summary>
    private static string? FindBestVersionedDirectory(string baseDir, Version targetVersion)
    {
        var candidates = Directory.GetDirectories(baseDir)
            .Select(d => (Path: d, Version: Version.TryParse(Path.GetFileName(d), out var v) ? v : null))
            .Where(x => x.Version != null)
            .ToList();

        if (candidates.Count == 0) return null;

        // Prefer exact match
        var exact = candidates.FirstOrDefault(x =>
            x.Version!.Major == targetVersion.Major &&
            x.Version.Minor == targetVersion.Minor &&
            x.Version.Build == targetVersion.Build);
        if (exact.Path != null) return exact.Path;

        // Fall back to same Major.Minor, highest patch
        return candidates
            .Where(x => x.Version!.Major == targetVersion.Major &&
                        x.Version.Minor == targetVersion.Minor)
            .OrderByDescending(x => x.Version!.Build)
            .Select(x => x.Path)
            .FirstOrDefault()
            // Finally fall back to highest version overall
            ?? candidates
                .OrderByDescending(x => x.Version!)
                .Select(x => x.Path)
                .FirstOrDefault();
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
