using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Diagnostics;
using DotNetOpenApiExtract.Core.Tests.SourceAnalysis;
using Microsoft.OpenApi;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Loading;

/// <summary>An operation whose types cannot be loaded is left out of the document with a warning, never silently.</summary>
public sealed class UnresolvedDependencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperationWithMissingDependency_IsReportedWhenLeftOut(bool removeContracts)
    {
        using var directory = new TempDirectory();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(TestPaths.PackageTypesApiDll)!))
            File.Copy(file, Path.Combine(directory.Path, Path.GetFileName(file)));
        if (removeContracts)
            File.Delete(Path.Combine(directory.Path, "PackageTypesContracts.dll"));

        var diagnostics = new List<ExtractionDiagnostic>();
        var document = OpenApiDocumentBuilder.Build(new OpenApiDocumentOptions
        {
            AssemblyPath = Path.Combine(directory.Path, "PackageTypesApi.dll"),
            OnDiagnostic = diagnostics.Add,
        });

        var operations = document.Paths.Values.SelectMany(p => ((OpenApiPathItem)p).Operations?.Keys.AsEnumerable() ?? []).ToList();
        var skipped = diagnostics.Where(d => d.Code == ExtractionDiagnosticCodes.OperationSkippedUnresolvedType).ToList();
        if (removeContracts)
        {
            operations.Should().BeEmpty();
            skipped.Should().ContainSingle();
            skipped[0].Location.Should().Be("GET /envelope");
            skipped[0].Subjects.Should().Equal("PackageTypesApi.Controllers.EnvelopeController.Get", "System.IO.FileNotFoundException");
        }
        else
        {
            operations.Should().ContainSingle();
            skipped.Should().BeEmpty();
        }
    }
}
