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
/// name elsewhere in the tree) is not counted in; a tie is decided by the checksums of the content, or
/// left undecided.
/// </para>
/// </remarks>
internal static class CompiledSourceFiles
{
    private static readonly Guid Sha1 = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    /// <summary>A source document of the PDB: its path with <c>/</c> separators and the checksum of its content.</summary>
    private sealed record PdbDocument(string Name, Guid HashAlgorithm, byte[] Hash);

    /// <summary>
    /// The files compiled into <paramref name="assemblyPath"/> among <paramref name="candidates"/> (files
    /// under <paramref name="sourceRoot"/>), as its PDB names them.
    /// </summary>
    /// <remarks>
    /// Several prefixes can explain the same number of files: a document <c>…/Api/Program.cs</c> matches
    /// both <c>Api/Program.cs</c> and a <c>Program.cs</c> at the root. The checksums the PDB records
    /// decide between them — the prefix whose files have the content that was compiled. When they do not
    /// decide, the result is <see cref="CompiledSourceLookup.Ambiguous"/>: any choice could be a copy the
    /// assembly does not contain.
    /// </remarks>
    public static CompiledSourceLookup Find(string assemblyPath, string sourceRoot, IEnumerable<string> candidates)
    {
        var documents = TryReadDocuments(assemblyPath);
        if (documents == null || documents.Count == 0)
            return CompiledSourceLookup.Unavailable;

        // Relative path (with '/') → full path of each candidate, grouped by file name for the matching.
        var byFileName = candidates
            .Select(full => (Full: full, Relative: Path.GetRelativePath(sourceRoot, full).Replace('\\', '/')))
            .ToLookup(f => FileName(f.Relative), StringComparer.Ordinal);

        // Each match of a document to a candidate votes for the prefix it implies.
        var matchesByPrefix = new Dictionary<string, List<(string Full, PdbDocument Document)>>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (var (full, relative) in byFileName[FileName(document.Name)])
            {
                if (!document.Name.EndsWith(relative, StringComparison.Ordinal))
                    continue;
                var prefix = document.Name[..^relative.Length];
                if (prefix.Length > 0 && !prefix.EndsWith('/'))
                    continue;
                if (!matchesByPrefix.TryGetValue(prefix, out var files))
                    matchesByPrefix[prefix] = files = [];
                files.Add((full, document));
            }
        }

        if (matchesByPrefix.Count == 0)
            return CompiledSourceLookup.Unavailable;

        var most = matchesByPrefix.Values.Max(f => f.Count);
        var tied = matchesByPrefix.Values.Where(f => f.Count == most).ToList();
        if (tied.Count > 1)
        {
            var scored = tied.Select(files => (Files: files, Same: files.Count(f => HasCompiledContent(f.Full, f.Document)))).ToList();
            var best = scored.Max(s => s.Same);
            tied = scored.Where(s => s.Same == best).Select(s => s.Files).ToList();
        }

        if (tied.Count > 1)
            return CompiledSourceLookup.Ambiguous(tied.SelectMany(f => f).Select(f => f.Full).Distinct(StringComparer.Ordinal).ToList());

        return CompiledSourceLookup.Found(tied[0].Select(f => f.Full).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>Whether the content of <paramref name="path"/> has the checksum the PDB records for <paramref name="document"/>.</summary>
    private static bool HasCompiledContent(string path, PdbDocument document)
    {
        try
        {
            byte[]? hash = document.HashAlgorithm == Sha256 ? System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))
                : document.HashAlgorithm == Sha1 ? System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(path))
                : null;
            return hash != null && hash.AsSpan().SequenceEqual(document.Hash);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The <c>.cs</c> documents of the portable PDB of <paramref name="assemblyPath"/>, with <c>/</c>
    /// separators; <see langword="null"/> when there is no readable portable PDB.
    /// </summary>
    private static List<PdbDocument>? TryReadDocuments(string assemblyPath)
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
                var documents = new List<PdbDocument>();
                foreach (var handle in reader.Documents)
                {
                    var document = reader.GetDocument(handle);
                    var name = reader.GetString(document.Name).Replace('\\', '/');
                    if (!name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        continue;
                    documents.Add(new PdbDocument(
                        name,
                        document.HashAlgorithm.IsNil ? Guid.Empty : reader.GetGuid(document.HashAlgorithm),
                        document.Hash.IsNil ? [] : reader.GetBlobBytes(document.Hash)));
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

/// <summary>What the PDB of an assembly says about the source files under a source root.</summary>
internal sealed class CompiledSourceLookup
{
    private CompiledSourceLookup(IReadOnlyList<string>? files, IReadOnlyList<string>? ambiguousFiles) =>
        (Files, AmbiguousFiles) = (files, ambiguousFiles);

    /// <summary>No portable PDB, or none of its documents is under the source root.</summary>
    public static CompiledSourceLookup Unavailable { get; } = new(null, null);

    /// <summary>The files compiled into the assembly.</summary>
    public static CompiledSourceLookup Found(IReadOnlyList<string> files) => new(files, null);

    /// <summary>The PDB matches several sets of files equally well; <paramref name="files"/> are all of them.</summary>
    public static CompiledSourceLookup Ambiguous(IReadOnlyList<string> files) => new(null, files);

    /// <summary>Full paths of the compiled files when they are known.</summary>
    public IReadOnlyList<string>? Files { get; }

    /// <summary>Full paths of the files of the equally good matches when the PDB does not decide.</summary>
    public IReadOnlyList<string>? AmbiguousFiles { get; }
}
