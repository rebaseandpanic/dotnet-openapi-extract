using System.Diagnostics;

namespace DotNetOpenApiExtract.Core.Tests.Harness;

/// <summary>Outcome of one CLI process run.</summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StdOut">Everything the process wrote to standard output.</param>
/// <param name="StdErr">Everything the process wrote to standard error.</param>
internal sealed record CliResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs the built <c>DotNetOpenApiExtract.Cli.dll</c> as a separate <c>dotnet</c> process,
/// so tests observe the real exit code and the real stdout/stderr streams.
/// </summary>
internal static class CliRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs the CLI with <paramref name="arguments"/> in <paramref name="workingDirectory"/>
    /// and waits for it to exit. A run that outlives <paramref name="timeout"/> is killed and
    /// reported as a <see cref="TimeoutException"/>, never as an exit code.
    /// <paramref name="environment"/> adds or overrides environment variables of the process.
    /// </summary>
    public static async Task<CliResult> RunAsync(
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            // The SDK sets DOTNET_HOST_PATH for processes it starts (test hosts included);
            // outside it, the dotnet on PATH runs the framework-dependent dll.
            FileName               = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            startInfo.Environment[name] = value;

        startInfo.ArgumentList.Add(TestPaths.CliDll);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the CLI process.");

        var stdOut = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"CLI did not exit within {timeout ?? DefaultTimeout}: {string.Join(' ', startInfo.ArgumentList)}");
        }

        return new CliResult(process.ExitCode, await stdOut, await stdErr);
    }
}
