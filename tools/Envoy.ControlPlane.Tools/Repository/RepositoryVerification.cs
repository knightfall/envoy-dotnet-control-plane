using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class RepositoryVerification
{
    private const string Solution = "Envoy.ControlPlane.slnx";

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            Require(args is ["verify"], "Usage: repository verify (run bindings generate first)");
            VerifyProjectInventory();
            await ValidationVerification.VerifyInputsAsync();
            await BuildProcess.RunAsync("dotnet", ["restore", Solution, "--locked-mode"]);
            await BuildProcess.RunAsync("dotnet", ["build", Solution, "--configuration", "Release", "--no-restore"]);
            await BuildProcess.RunAsync("dotnet", ["test", "--solution", Solution, "--configuration", "Release", "--no-build", "--no-restore",
                "--results-directory", Path.GetFullPath(BindingPaths.InputsDirectory + "/results/repository"), "--report-xunit-trx"]);
            CopyLifecycleReports();
            int cases = await ValidationVerification.CompareWithGoAsync();
            await ResourceVerification.CompareWithGoAsync();
            await PreviewHelperVerification.CompareWithGoAsync();
            await SnapshotCacheVerification.CompareWithGoAsync();
            await SotwSubscriptionVerification.CompareWithGoAsync();
            await DeltaSubscriptionVerification.CompareWithGoAsync();
            await SotwStreamVerification.CompareWithGoAsync();
            await BuildProcess.RunAsync("dotnet", ["pack", Solution, "--configuration", "Release", "--no-build", "--no-restore",
                "--output", BindingPaths.PackageDirectory]);
            PackageContentsVerification.Verify();
            await PackageVerification.VerifyCleanConsumerAsync();
            await PackageVerification.VerifyCleanConsumerAsync(ValidationPaths.TestsProject, "validation-consumer");
            await PackageVerification.VerifyCleanConsumerAsync("tests/Envoy.ControlPlane.Tests/Envoy.ControlPlane.Tests.csproj", "core-consumer");
            await PackageVerification.VerifyCleanConsumerAsync("tests/Envoy.ControlPlane.AspNetCore.Tests/Envoy.ControlPlane.AspNetCore.Tests.csproj", "hosting-consumer");
            await RootConsumerVerification.RunAsync("Core");
            await RootConsumerVerification.RunAsync("AspNetCore");
            await ReproducibilityReport.WriteAsync(Path.Combine(BindingPaths.InputsDirectory, "results", "reproducibility", "snapshot.json"));
            Console.WriteLine($"Repository build, tests, packages, consumers and reproducibility report passed; {cases} Go PGV cases and the snapshot/cache/SotW-and-Delta-subscription/stream corpora matched. Delta delivery, Fetch, full GV and the separately invoked released-Envoy command remain outside this verification command.");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void CopyLifecycleReports()
    {
        string output = Path.Combine(BindingPaths.InputsDirectory, "results", "lifecycle");
        Directory.CreateDirectory(output);
        foreach (string mode in new[] { "ordered", "unordered" })
        {
            string name = $"sotw-stress-{mode}.json";
            string source = Path.Combine("tests", "Envoy.ControlPlane.AspNetCore.Tests", "bin", "Release", "net10.0", name);
            Require(File.Exists(source), "Missing SotW lifecycle report: " + source);
            File.Copy(source, Path.Combine(output, name), overwrite: true);
        }
    }

    private static void VerifyProjectInventory()
    {
        var registered = XDocument.Load(Solution).Descendants("Project")
            .Select(project => project.Attribute("Path")!.Value.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        var maintained = new[] { "src", "tests", "tools", "examples" }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Select(path => path.Replace('\\', '/'))
            .Where(path => !path.StartsWith("tests/validation/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        Require(registered.SetEquals(maintained), "Solution project inventory differs from maintained source projects");
        Console.WriteLine($"Solution includes all {registered.Count} maintained projects; historical provider probes are excluded.");
    }
}
