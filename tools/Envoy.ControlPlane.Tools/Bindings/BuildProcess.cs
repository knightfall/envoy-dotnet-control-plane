using System.Diagnostics;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class BuildProcess
{
    internal static async Task<string> RunAsync(string executable, string[] arguments, string? directory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                start.Environment[entry.Key] = entry.Value;
            }
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException(executable + " did not complete");
        }

        string output = await stdout;
        string error = await stderr;
        Console.Write(output);
        Console.Error.Write(error);
        Require(process.ExitCode == 0, executable + " exited " + process.ExitCode);
        return output;
    }
}
