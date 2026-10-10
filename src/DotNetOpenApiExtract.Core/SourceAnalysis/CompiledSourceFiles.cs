using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// The source files under a source root that were compiled into an assembly, read from the
/// <c>Document</c> table of its portable PDB (next to the assembly or embedded in it).
/// </summary>
/// <remarks>
/// A file under the source root that is not a document of the PDB is not in the assembly: a copy such
/// as <c>Program.Old.cs</c>, a file excluded with <c>&lt;Compile Remove&gt;</c>, a file added after the
/// build. Reading it would describe code the assembly does not contain.
/// <para>
/// The PDB names documents by the paths of the build: absolute paths of another machine, or paths
/// rewritten by <c>PathMap</c> (<c>/_/…</c> in deterministic CI builds). They are matched to the source
/// root by their tail: a document <c>D</c> matches the file at relative path <c>R</c> when <c>D</c> is
/// <c>P + R</c> for some prefix <c>P</c>, the build-side location of the source root. The prefix shared
/// by the most files is taken, so a file that matches only under another prefix (a project of the same
/// name elsewhere in the tree) is not counted in.
/// </para>
/// </remarks>
internal static class CompiledSourceFiles
{
    /// <summary>
    /// The full paths of the <paramref name="candidates"/> (files under <paramref name="sourceRoot"/>)
    /// that the PDB of <paramref name="assemblyPath"/> names as documents; <see langword="null"/> when the
    /// assembly has no portable PDB or none of its documents matches a candidate.
    /// </summary>
    public static IReadOnlyList<string>? TryFind(string assemblyPath, string sourceRoot, IEnumerable<string> candidates)
    {
        var documents = TryReadDocuments(assemblyPath);
        if (documents == null || documents.Count == 0)
            return null;

        // Relative path (with '/') → full path of each candidate, grouped by file name for the matching.
        var byFileName = candidates
            .Select(full => (Full: full, Relative: Path.GetRelativePath(sourceRoot, full).Replace('\\', '/')))
            .ToLookup(f => FileName(f.Relative), StringComparer.Ordinal);

        // Each match of a document to a candidate votes for the prefix it implies.
        var matchesByPrefix = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var (full, relative) in byFileName[FileName(document)])
            {
                if (!document.EndsWith(relative, StringComparison.Ordinal))
                    continue;
                var prefix = document[..^relative.Length];
                if (prefix.Length > 0 && !prefix.EndsWith('/'))
                    continue;
                if (!matchesByPrefix.TryGetValue(prefix, out var files))
                    matchesByPrefix[prefix] = files = [];
                files.Add(full);
            }
        }

        if (matchesByPrefix.Count == 0)
            return null;

        // The most files; on a tie the longest prefix, then ordinal: the result does not depend on the order of the table.
        return matchesByPrefix
            .OrderByDescending(p => p.Value.Count)
            .ThenByDescending(p => p.Key.Length)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .First().Value
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The <c>.cs</c> document names of the portable PDB of <paramref name="assemblyPath"/>, with <c>/</c>
    /// separators; <see langword="null"/> when there is no readable portable PDB.
    /// </summary>
    private static List<string>? TryReadDocuments(string assemblyPath)
    {
        try
        {
            using var peStream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(peStream);
            if (!peReader.TryOpenAssociatedPortablePdb(
                    assemblyPath,
                    path => File.Exists(path) ? File.OpenRead(path) : null,
                    out var provider,
                    out _)
                || provider == null)
                return null;

            using (provider)
            {
                var reader = provider.GetMetadataReader();
                var documents = new List<string>();
                foreach (var handle in reader.Documents)
                {
                    var name = reader.GetString(reader.GetDocument(handle).Name).Replace('\\', '/');
                    if (name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        documents.Add(name);
                }

                return documents;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            // A Windows (non-portable) PDB, a damaged file: no list of compiled files.
            return null;
        }
    }

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];
}
