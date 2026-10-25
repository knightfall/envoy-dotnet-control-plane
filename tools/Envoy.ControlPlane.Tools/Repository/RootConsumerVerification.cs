using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class RootConsumerVerification
{
    internal static async Task RunAsync(string name)
    {
        string repository = Path.GetFullPath(".");
        string scratch = Path.Combine(Path.GetTempPath(), "envoy-root-consumer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            foreach (string file in new[] { "global.json", "Directory.Build.props", "Directory.Packages.props", ".editorconfig" })
            {
                File.Copy(file, Path.Combine(scratch, file));
            }
            File.Copy($"tests/consumers/{name}/Consumer.csproj.template", Path.Combine(scratch, "Consumer.csproj"));
            File.Copy($"tests/consumers/{name}/Program.cs", Path.Combine(scratch, "Program.cs"));
            File.Copy("tests/consumers/ConsumerChecks.cs", Path.Combine(scratch, "ConsumerChecks.cs"));
            PackageVerification.WriteConsumerPackageSources(scratch, repository);
            string cache = Path.Combine(scratch, "obj", "packages");
            await BuildProcess.RunAsync("dotnet", ["restore", "Consumer.csproj", "--packages", cache], scratch);
            await BuildProcess.RunAsync("dotnet", ["restore", "Consumer.csproj", "--locked-mode", "--packages", cache], scratch);
            await BuildProcess.RunAsync("dotnet", ["run", "--project", "Consumer.csproj", "--configuration", "Release", "--no-restore"], scratch);

            string report = Path.Combine(BindingPaths.InputsDirectory, "results", "root-consumers", name);
            Directory.CreateDirectory(report);
            File.Copy(Path.Combine(scratch, "packages.lock.json"), Path.Combine(report, "packages.lock.json"), overwrite: true);
            File.Copy(Path.Combine(scratch, "bin/Release/net10.0/Consumer.runtimeconfig.json"), Path.Combine(report, "runtimeconfig.json"), overwrite: true);
            Require(!File.ReadAllText(Path.Combine(scratch, "Consumer.csproj")).Contains("ProjectReference", StringComparison.Ordinal),
                "Root package consumer must not reference source projects");
            Console.WriteLine(name + " package consumer passed with a fresh NuGet cache.");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
