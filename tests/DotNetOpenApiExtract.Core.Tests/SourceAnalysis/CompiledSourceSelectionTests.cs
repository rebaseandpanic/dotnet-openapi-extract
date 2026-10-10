using System.Text.Json.Nodes;
using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.Harness;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

/// <summary>
/// The configuration comes from the source files the assembly was built from. A file under the source
/// root that is not compiled into it — an older copy of Program.cs, a file excluded with
/// <c>&lt;Compile Remove&gt;</c> — is not read: the document describes the code in the assembly.
/// StrayEntryApi is built from a Program.cs that declares the security scheme <c>Real</c>; its
/// Program.Legacy.cs, excluded with <c>&lt;Compile Remove="Program.*.cs" /&gt;</c>, declares <c>Stray</c>.
/// </summary>
public class CompiledSourceSelectionTests
{
    private static string FixtureDirectory => Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
        Path.GetDirectoryName(TestPaths.StrayEntryApiDll)!)!)!)!;

    private static string StrayProgram => File.ReadAllText(Path.Combine(FixtureDirectory, "Program.Legacy.cs"));

    private static (JsonNode Document, IReadOnlyList<ExtractionDiagnostic> Diagnostics) Build(string assemblyPath, string? sourceRoot)
    {
        var (document, diagnostics) = VersionedDocumentHarness.BuildCollecting(onDiagnostic => new OpenApiDocumentOptions
        {
            AssemblyPath   = assemblyPath,
            SourceRoot     = sourceRoot,
            OpenApiVersion = OpenApiSpecVersion.OpenApi3_1,
            OnDiagnostic   = onDiagnostic,
        });
        return (JsonNode.Parse(document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, CancellationToken.None).GetAwaiter().GetResult())!, diagnostics);
    }

    private static IEnumerable<string> SchemeNames(JsonNode document) =>
        document["components"]?["securitySchemes"]?.AsObject().Select(p => p.Key) ?? [];

    /// <summary>A source root holding the fixture's Program.cs and controller, plus <paramref name="extraFiles"/> (name → text).</summary>
    private static TempDirectory SourceRootWith(params (string Name, string Text)[] extraFiles)
    {
        var directory = new TempDirectory();
        File.Copy(Path.Combine(FixtureDirectory, "Program.cs"), Path.Combine(directory.Path, "Program.cs"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "Controllers"));
        File.Copy(Path.Combine(FixtureDirectory, "Controllers", "ItemsController.cs"), Path.Combine(directory.Path, "Controllers", "ItemsController.cs"));
        foreach (var (name, text) in extraFiles)
        {
            var path = Path.Combine(directory.Path, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        return directory;
    }

    /// <summary>The fixture's build output without its PDB: the compiled files are unknown.</summary>
    private static string DllWithoutPdb => OutputWithoutPdb.Of(TestPaths.StrayEntryApiDll);

    [Fact]
    public void FileExcludedWithCompileRemove_IsNotRead()
    {
        var (document, diagnostics) = Build(TestPaths.StrayEntryApiDll, sourceRoot: null);

        SchemeNames(document).Should().Equal("Real");
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SourceEntryPointAmbiguous);
    }

    public static TheoryData<string> StrayFileNames =>
        ["Program.Old.cs", "Program.Legacy.cs", "Program.Z.cs", "ProgramOld.cs", "OldProgram.cs", "Startup.cs", "Old/Program.cs"];

    /// <summary>The PDB names Program.cs only: a copy under any name is not read, without a warning.</summary>
    [Theory]
    [MemberData(nameof(StrayFileNames))]
    public void CopyNotInThePdb_IsNotRead(string strayName)
    {
        using var sourceRoot = SourceRootWith((strayName, StrayProgram));

        var (document, diagnostics) = Build(TestPaths.StrayEntryApiDll, sourceRoot.Path);

        SchemeNames(document).Should().Equal("Real");
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SourceEntryPointAmbiguous);
    }

    private static string RealProgram => File.ReadAllText(Path.Combine(FixtureDirectory, "Program.cs"));

    /// <summary>
    /// A source root above the project, holding the project's Program.cs one level down and a copy at its
    /// root: the PDB document <c>…/StrayEntryApi/Program.cs</c> matches both equally by path, and its
    /// checksum picks the compiled one.
    /// </summary>
    [Fact]
    public void PathTieInThePdb_IsDecidedByTheChecksum()
    {
        using var sourceRoot = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot.Path, "StrayEntryApi"));
        File.Copy(Path.Combine(FixtureDirectory, "Program.cs"), Path.Combine(sourceRoot.Path, "StrayEntryApi", "Program.cs"));
        File.WriteAllText(Path.Combine(sourceRoot.Path, "Program.cs"), StrayProgram);

        var (document, diagnostics) = Build(TestPaths.StrayEntryApiDll, sourceRoot.Path);

        SchemeNames(document).Should().Equal("Real");
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SourceCompiledFilesAmbiguous);
    }

    /// <summary>
    /// The same tie with the project's Program.cs edited after the build: no content matches the PDB, so
    /// neither file is read, with a warning naming both.
    /// </summary>
    [Fact]
    public void PathTieInThePdb_NotDecidedByTheChecksum_NothingIsRead_WithAWarning()
    {
        using var sourceRoot = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot.Path, "StrayEntryApi"));
        File.WriteAllText(Path.Combine(sourceRoot.Path, "StrayEntryApi", "Program.cs"), RealProgram + "\n// edited after the build\n");
        File.WriteAllText(Path.Combine(sourceRoot.Path, "Program.cs"), StrayProgram);

        var (document, diagnostics) = Build(TestPaths.StrayEntryApiDll, sourceRoot.Path);

        SchemeNames(document).Should().BeEmpty();
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SourceCompiledFilesAmbiguous)
            .Which.Subjects.Should().Equal("Program.cs", "StrayEntryApi/Program.cs");
    }

    /// <summary>
    /// Without a PDB the compiled files are unknown: of several entry-point files the nearest Program.cs is
    /// read, with a warning naming the candidates.
    /// </summary>
    [Theory]
    [MemberData(nameof(StrayFileNames))]
    public void WithoutPdb_SeveralEntryPointFiles_ProgramCsIsRead_WithAWarning(string strayName)
    {
        using var sourceRoot = SourceRootWith((strayName, StrayProgram));

        var (document, diagnostics) = Build(DllWithoutPdb, sourceRoot.Path);

        SchemeNames(document).Should().Equal("Real");
        var warning = diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SourceEntryPointAmbiguous).Subject;
        warning.Subjects.Should().Contain(["Program.cs", strayName]);
    }

    /// <summary>Without a PDB and with one entry-point file, it is read without a warning.</summary>
    [Fact]
    public void WithoutPdb_OneEntryPointFile_IsRead_WithoutAWarning()
    {
        using var sourceRoot = SourceRootWith();

        var (document, diagnostics) = Build(DllWithoutPdb, sourceRoot.Path);

        SchemeNames(document).Should().Equal("Real");
        diagnostics.Should().NotContain(d => d.Code == ExtractionDiagnosticCodes.SourceEntryPointAmbiguous);
    }

    /// <summary>
    /// Without a PDB and without a Program.cs among several entry-point files, none is read: any choice
    /// could describe code the assembly does not contain.
    /// </summary>
    [Fact]
    public void WithoutPdb_SeveralEntryPointFilesAndNoProgramCs_NoneIsRead_WithAWarning()
    {
        using var sourceRoot = new TempDirectory();
        File.Copy(Path.Combine(FixtureDirectory, "Program.cs"), Path.Combine(sourceRoot.Path, "Startup.cs"));
        File.WriteAllText(Path.Combine(sourceRoot.Path, "Program.Old.cs"), StrayProgram);

        var (document, diagnostics) = Build(DllWithoutPdb, sourceRoot.Path);

        SchemeNames(document).Should().BeEmpty();
        diagnostics.Should().ContainSingle(d => d.Code == ExtractionDiagnosticCodes.SourceEntryPointAmbiguous)
            .Which.Subjects.Should().Equal("Program.Old.cs", "Startup.cs");
    }
}
