using System.Diagnostics;

namespace Envoy.ControlPlane.EnvoyProbe;

internal sealed class EnvoyProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> stdout;
    private readonly Task<string> stderr;
    private readonly string directory;
    internal int ExitCode => process.ExitCode;

    internal EnvoyProcess(string binary, string bootstrap, string directory)
    {
        this.directory = directory;
        var start = new ProcessStartInfo(binary) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-c", bootstrap, "--disable-hot-restart", "--concurrency", "1", "--log-level", "info" })
        {
            start.ArgumentList.Add(argument);
        }
        process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Envoy");
        stdout = process.StandardOutput.ReadToEndAsync();
        stderr = process.StandardError.ReadToEndAsync();
    }

    internal void EnsureRunning()
    {
        if (process.HasExited) throw new InvalidOperationException($"Envoy exited {process.ExitCode}; inspect {directory}/envoy-stderr.log");
    }

    internal async Task WaitForExitAsync(CancellationToken cancellationToken) => await process.WaitForExitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(Path.Combine(directory, "envoy-stdout.log"), await stdout);
        await File.WriteAllTextAsync(Path.Combine(directory, "envoy-stderr.log"), await stderr);
        process.Dispose();
    }
}
