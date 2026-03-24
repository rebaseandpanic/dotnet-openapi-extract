using AwesomeAssertions;
using Xunit;

namespace DotNetOpenApiExtract.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void ProjectLoads()
    {
        // Verify the test infrastructure works
        true.Should().BeTrue();
    }
}
