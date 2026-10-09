using Xunit;

namespace DotNetOpenApiExtract.Core.Tests.Diagnostics;

/// <summary>
/// Tests that replace <see cref="Console.Error"/> run alone: the writer is process-wide, and a
/// test running in parallel would write into the captured stream or lose its own output.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleErrorCollection
{
    public const string Name = "Console.Error capture";
}
