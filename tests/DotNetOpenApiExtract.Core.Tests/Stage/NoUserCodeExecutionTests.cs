using AwesomeAssertions;
using DotNetOpenApiExtract.Core.Tests.Harness;
using ModernApi.Models.Probes;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Stage;

/// <summary>
/// Extraction never executes code of the analysed assembly: building ModernApi in every version
/// leaves none of the markers its probes (static constructor, converter constructor, attribute
/// constructor) write when they run.
/// </summary>
public class NoUserCodeExecutionTests
{
    [Fact]
    public void Build_AllVersions_RunsNoProbe()
    {
        var markerDirectory = Path.Combine(Path.GetTempPath(), ProbeMarker.DirectoryName);
        if (Directory.Exists(markerDirectory))
            Directory.Delete(markerDirectory, recursive: true);

        foreach (var version in VersionedDocumentHarness.Versions)
        {
            var document = VersionedDocumentHarness.Build(VersionedDocumentHarness.ModernApiOptions(version));
            document.Components!.Schemas!.Should().ContainKey(nameof(ExecutionProbeDto),
                because: "the probes must be reached by the build, or the test proves nothing");
        }

        Directory.Exists(markerDirectory).Should().BeFalse();
    }
}
