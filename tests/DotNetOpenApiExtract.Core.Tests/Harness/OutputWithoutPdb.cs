using System.Collections.Concurrent;

namespace DotNetOpenApiExtract.Core.Tests.Harness;

/// <summary>
/// A copy of a fixture's build output without its PDB, made once per test process and deleted at exit.
/// Without a PDB the files compiled into the assembly are unknown, so every <c>.cs</c> file under the
/// source root is read: the setup for a test whose source root is not the one the fixture was built from.
/// </summary>
internal static class OutputWithoutPdb
{
    private static readonly ConcurrentDictionary<string, Lazy<string>> Copies = new(StringComparer.Ordinal);

    /// <summary>The path of the copy of <paramref name="assemblyPath"/>, its output directory copied without <c>*.pdb</c>.</summary>
    public static string Of(string assemblyPath) =>
        Copies.GetOrAdd(assemblyPath, path => new Lazy<string>(() => Copy(path))).Value;

    private static string Copy(string assemblyPath)
    {
        var directory = Directory.CreateTempSubdirectory("dotnet_openapi_extract_nopdb_").FullName;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };

        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(assemblyPath)!))
        {
            if (!file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }

        return Path.Combine(directory, Path.GetFileName(assemblyPath));
    }
}
