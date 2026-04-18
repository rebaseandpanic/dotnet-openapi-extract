using System.Xml.Linq;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Resolves the source root directory for a compiled .NET assembly by walking up
/// the file system from the assembly path until a matching <c>.csproj</c> file is found.
/// </summary>
public static class SourceRootResolver
{
    /// <summary>
    /// Attempts to find the source root directory (the folder containing the <c>.csproj</c>)
    /// for the given assembly.
    /// </summary>
    /// <param name="assemblyPath">Absolute path to the compiled assembly (.dll).</param>
    /// <param name="sourceRoot">
    /// When this method returns <see langword="true"/>, contains the directory that holds
    /// the matching <c>.csproj</c> file. When <see langword="false"/>, this is
    /// <see langword="null"/>.
    /// </param>
    /// <param name="failureReason">
    /// When this method returns <see langword="false"/>, contains a human-readable
    /// description of why the source root could not be determined. When
    /// <see langword="true"/>, this is <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a unique matching <c>.csproj</c> was found;
    /// <see langword="false"/> if no project file was found or the match was ambiguous.
    /// This method never throws.
    /// </returns>
    public static bool TryResolve(
        string assemblyPath,
        out string? sourceRoot,
        out string? failureReason)
    {
        sourceRoot = null;
        failureReason = null;

        try
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
            var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!);

            while (dir != null)
            {
                var csprojFiles = dir.GetFiles("*.csproj");
                if (csprojFiles.Length > 0)
                {
                    var matched = TryMatchCsproj(csprojFiles, assemblyName, out failureReason);
                    if (matched != null)
                    {
                        sourceRoot = matched.DirectoryName!;
                        failureReason = null;
                        return true;
                    }

                    // failureReason is set by TryMatchCsproj
                    return false;
                }

                dir = dir.Parent;
            }

            // No .csproj found anywhere — this is a normal case (custom build layout)
            return false;
        }
        catch (Exception ex)
        {
            failureReason = $"Unexpected error while resolving source root: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Selects the best matching <c>.csproj</c> from the candidates for the given assembly name.
    /// </summary>
    /// <param name="candidates">All .csproj files found in the directory.</param>
    /// <param name="assemblyName">The assembly name (without extension) to match.</param>
    /// <param name="failureReason">Set when the match is ambiguous; otherwise null.</param>
    /// <returns>The matched <see cref="FileInfo"/>, or null if no match or ambiguous.</returns>
    private static FileInfo? TryMatchCsproj(
        FileInfo[] candidates,
        string assemblyName,
        out string? failureReason)
    {
        failureReason = null;

        if (candidates.Length == 1)
            return candidates[0];

        // Step 1: match by filename (without extension) == assemblyName
        var byName = candidates
            .Where(f => string.Equals(
                Path.GetFileNameWithoutExtension(f.Name),
                assemblyName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byName.Count == 1)
            return byName[0];

        if (byName.Count > 1)
        {
            failureReason = $"Multiple .csproj files match assembly name '{assemblyName}' by filename. " +
                            "Use --source-root to specify the project directory explicitly.";
            return null;
        }

        // Step 2: check <AssemblyName> overrides in each csproj
        var byAssemblyNameOverride = candidates
            .Where(f => ReadAssemblyNameFromCsproj(f.FullName) is { } an &&
                        string.Equals(an, assemblyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (byAssemblyNameOverride.Count == 1)
            return byAssemblyNameOverride[0];

        if (byAssemblyNameOverride.Count > 1)
        {
            failureReason = $"Multiple .csproj files declare <AssemblyName>{assemblyName}</AssemblyName>. " +
                            "Use --source-root to specify the project directory explicitly.";
            return null;
        }

        // No match at all
        failureReason = $"Found {candidates.Length} .csproj file(s) in the directory but none " +
                        $"matches assembly name '{assemblyName}' (by filename or <AssemblyName> element). " +
                        "Use --source-root to specify the project directory explicitly.";
        return null;
    }

    /// <summary>
    /// Reads the <c>&lt;AssemblyName&gt;</c> value from a <c>.csproj</c> file, if present.
    /// Returns <see langword="null"/> if the element is absent or the file cannot be read.
    /// </summary>
    private static string? ReadAssemblyNameFromCsproj(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            return doc.Descendants("AssemblyName").FirstOrDefault()?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
