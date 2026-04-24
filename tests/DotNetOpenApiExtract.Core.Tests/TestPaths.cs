namespace DotNetOpenApiExtract.Core.Tests;

/// <summary>
/// Resolves paths to test assemblies relative to the solution root.
/// </summary>
internal static class TestPaths
{
    private static readonly Lazy<string> _solutionRoot = new(FindSolutionRoot);

    public static string SolutionRoot => _solutionRoot.Value;

    public static string SampleApiDll        => FindFixtureDll("SampleApi");

    public static string SampleApiXml        => Path.ChangeExtension(SampleApiDll, ".xml");

    /// <summary>
    /// Path to the MinimalProductApi test fixture DLL. Declares <c>[AssemblyProduct]</c>
    /// and <c>[AssemblyCompany]</c> but NOT <c>[AssemblyTitle]</c> — used to exercise the
    /// title resolution fall-back to <c>[AssemblyProduct]</c>.
    /// </summary>
    public static string MinimalProductApiDll => FindFixtureDll("MinimalProductApi");

    /// <summary>
    /// Path to the BareMinimalApi test fixture DLL. Declares no identity assembly
    /// attributes — used to exercise the title fall-back to <c>assembly.GetName().Name</c>
    /// and the contact gate when <c>[AssemblyCompany]</c> is absent.
    /// </summary>
    public static string BareMinimalApiDll   => FindFixtureDll("BareMinimalApi");

    /// <summary>
    /// Resolves a fixture DLL by convention: <c>tests/TestAssemblies/{name}/bin/**/{name}.dll</c>.
    /// Prefers Debug over Release, newest TFM.
    /// </summary>
    private static string FindFixtureDll(string name)
    {
        var baseDir = Path.Combine(SolutionRoot, "tests", "TestAssemblies", name, "bin");
        if (!Directory.Exists(baseDir))
            throw new DirectoryNotFoundException(
                $"{name} bin directory not found: {baseDir}. Build {name} first.");

        var candidates = Directory.GetFiles(baseDir, $"{name}.dll", SearchOption.AllDirectories);
        if (candidates.Length == 0)
            throw new FileNotFoundException(
                $"{name}.dll not found under {baseDir}. Build {name} first.");

        return candidates
            .OrderByDescending(p => p.Contains("Debug", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(p => p)
            .First();
    }

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
