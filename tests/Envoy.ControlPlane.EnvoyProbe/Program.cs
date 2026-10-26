using System.Diagnostics;

namespace Envoy.ControlPlane.EnvoyProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is not [var binary, var template, var output]) throw new ArgumentException("Usage: EnvoyProbe <envoy-binary> <bootstrap-template> <output-directory>");
            Directory.CreateDirectory(output);
            var start = new ProcessStartInfo(binary) { RedirectStandardOutput = true };
            start.ArgumentList.Add("--version");
            using var version = Process.Start(start) ?? throw new InvalidOperationException("Could not read Envoy version");
            string text = await version.StandardOutput.ReadToEndAsync();
            await version.WaitForExitAsync();
            if (version.ExitCode != 0) throw new InvalidOperationException("Envoy version command failed");
            await File.WriteAllTextAsync(Path.Combine(output, "envoy-version.txt"), text);
            await EnvoyScenario.RunAsync(binary, template, output, ordered: false);
            await EnvoyScenario.RunAsync(binary, template, output, ordered: true);
            await DeltaEnvoyScenario.RunAsync(binary, template, output);
            await TtlEnvoyScenario.RunAsync(binary, template, output);
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
