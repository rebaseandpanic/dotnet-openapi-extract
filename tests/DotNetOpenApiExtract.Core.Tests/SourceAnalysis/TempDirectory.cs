namespace DotNetOpenApiExtract.Core.Tests.SourceAnalysis;

/// <summary>
/// Creates a temporary directory and deletes it (with all contents) when disposed.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = Directory.CreateTempSubdirectory("dotnet_openapi_extract_test_").FullName;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; do not throw from Dispose
        }
    }
}
