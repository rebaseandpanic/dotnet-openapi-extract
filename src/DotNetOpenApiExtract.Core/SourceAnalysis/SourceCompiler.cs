using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotNetOpenApiExtract.Core.SourceAnalysis;

/// <summary>
/// Creates a Roslyn <see cref="CSharpCompilation"/> from all <c>.cs</c> source files
/// found under a given source root directory.
/// </summary>
/// <remarks>
/// The compilation is built without full framework references — only the core
/// library (<c>System.Private.CoreLib</c>) and the <c>System.Runtime</c> facade are
/// added so that primitive types resolve and in-project <c>const string</c> members
/// can be folded by the semantic model. This is intentional: the goal is syntax-level
/// analysis and literal / constant extraction, not a complete semantic build.
/// </remarks>
public static class SourceCompiler
{
    /// <summary>
    /// Directories (relative, case-insensitive) that are excluded from source scanning
    /// to avoid picking up generated code from <c>bin/</c> and <c>obj/</c> outputs.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        "node_modules",
        ".git",
    };

    /// <summary>
    /// Compiles all eligible <c>.cs</c> files under <paramref name="sourceRoot"/> into
    /// a <see cref="SourceCompilationResult"/>.
    /// </summary>
    /// <param name="sourceRoot">
    /// The root directory of the project (the folder that contains the <c>.csproj</c>).
    /// </param>
    /// <returns>
    /// A <see cref="SourceCompilationResult"/> containing the compilation and its syntax trees.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourceRoot"/> is null, empty, or does not exist on disk.
    /// </exception>
    public static SourceCompilationResult Compile(string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new ArgumentException("sourceRoot must not be null or empty.", nameof(sourceRoot));
        if (!Directory.Exists(sourceRoot))
            throw new ArgumentException($"Source root directory does not exist: {sourceRoot}", nameof(sourceRoot));

        return Compile(sourceRoot, EnumerateCsFiles(sourceRoot));
    }

    /// <summary>
    /// Compiles <paramref name="files"/>, files under <paramref name="sourceRoot"/>, into a
    /// <see cref="SourceCompilationResult"/>: the files of the project that were compiled into the
    /// assembly, when they are known.
    /// </summary>
    /// <param name="sourceRoot">The root directory of the project.</param>
    /// <param name="files">Full paths of the <c>.cs</c> files to compile.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="sourceRoot"/> is null, empty, or does not exist on disk.
    /// </exception>
    public static SourceCompilationResult Compile(string sourceRoot, IEnumerable<string> files)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new ArgumentException("sourceRoot must not be null or empty.", nameof(sourceRoot));
        if (!Directory.Exists(sourceRoot))
            throw new ArgumentException($"Source root directory does not exist: {sourceRoot}", nameof(sourceRoot));
        ArgumentNullException.ThrowIfNull(files);

        var csFiles = files;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTrees = csFiles
            .Select(file =>
            {
                var source = File.ReadAllText(file);
                return CSharpSyntaxTree.ParseText(source, parseOptions, path: file);
            })
            .ToList();

        // Minimal MetadataReferences: core library + System.Runtime for basic type resolution.
        // We deliberately don't reference ASP.NET Core assemblies — this compilation
        // is for syntax analysis only.
        var references = BuildMinimalReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: Path.GetFileName(sourceRoot),
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return new SourceCompilationResult(sourceRoot, compilation, syntaxTrees);
    }

    /// <summary>
    /// Enumerates all <c>.cs</c> files under <paramref name="sourceRoot"/>,
    /// excluding directories listed in <see cref="ExcludedDirectoryNames"/>.
    /// </summary>
    internal static IEnumerable<string> EnumerateCsFiles(string sourceRoot)
    {
        return EnumerateFilesExcluding(sourceRoot);
    }

    private static IEnumerable<string> EnumerateFilesExcluding(string directory)
    {
        // Yield .cs files directly in this directory
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
            yield return file;

        // Recurse into subdirectories, skipping excluded ones
        foreach (var subDir in Directory.EnumerateDirectories(directory))
        {
            var dirName = Path.GetFileName(subDir);
            if (ExcludedDirectoryNames.Contains(dirName))
                continue;

            foreach (var file in EnumerateFilesExcluding(subDir))
                yield return file;
        }
    }

    /// <summary>
    /// Builds a minimal set of <see cref="MetadataReference"/>s needed to resolve
    /// primitive types: the core library of the current runtime plus the
    /// <c>System.Runtime</c> / <c>mscorlib</c> / <c>netstandard</c> facades.
    /// </summary>
    private static IReadOnlyList<MetadataReference> BuildMinimalReferences()
    {
        var coreLibPath = typeof(object).Assembly.Location;
        var runtimeDir = Path.GetDirectoryName(coreLibPath)!;

        var references = new List<MetadataReference>
        {
            // System.Private.CoreLib — the assembly that actually defines System.Object,
            // System.String, etc. In the runtime directory System.Runtime.dll is only a
            // facade that type-forwards these types here; without this reference the
            // compilation has no core library, `string` does not bind, and
            // SemanticModel.GetConstantValue cannot fold in-project `const string` members.
            MetadataReference.CreateFromFile(coreLibPath),
        };

        // System.Runtime — the facade that source-level type names resolve through.
        TryAddReference(references, runtimeDir, "System.Runtime.dll");
        // mscorlib / netstandard for older project styles
        TryAddReference(references, runtimeDir, "mscorlib.dll");
        TryAddReference(references, runtimeDir, "netstandard.dll");

        return references;
    }

    private static void TryAddReference(List<MetadataReference> references, string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
            references.Add(MetadataReference.CreateFromFile(path));
    }
}
