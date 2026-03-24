namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Resolves paths to test assemblies relative to the solution root.
/// </summary>
internal static class TestPaths
{
    private static readonly Lazy<string> _solutionRoot = new(FindSolutionRoot);

    public static string SolutionRoot => _solutionRoot.Value;

    public static string SampleApiDll
    {
        get
        {
            // Try current configuration and TFM dynamically
            var baseDir = Path.Combine(SolutionRoot, "tests", "TestAssemblies", "SampleApi", "bin");
            if (!Directory.Exists(baseDir))
                throw new DirectoryNotFoundException($"SampleApi bin directory not found: {baseDir}. Build SampleApi first.");

            // Search for SampleApi.dll in any configuration/TFM combination
            var candidates = Directory.GetFiles(baseDir, "SampleApi.dll", SearchOption.AllDirectories);
            if (candidates.Length == 0)
                throw new FileNotFoundException($"SampleApi.dll not found under {baseDir}. Build SampleApi first.");

            // Prefer Debug over Release, newest TFM
            return candidates
                .OrderByDescending(p => p.Contains("Debug", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(p => p)
                .First();
        }
    }

    public static string SampleApiXml => Path.ChangeExtension(SampleApiDll, ".xml");

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find solution root from {AppContext.BaseDirectory}. " +
            "Make sure the solution file exists.");
    }
}
