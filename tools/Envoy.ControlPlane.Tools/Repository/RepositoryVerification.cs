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
            Require(args is ["verify"] or ["verify", "--coverage"], "Usage: repository verify [--coverage] (run bindings generate first)");
            VerifyProjectInventory();
            string[] testProjects = DiscoverDockerFreeTestProjects(Solution);
            Require(testProjects.Length > 0, "The solution has no declared Docker-free test projects");
            await ValidationVerification.VerifyInputsAsync();
            await BuildProcess.RunAsync("dotnet", ["restore", Solution, "--locked-mode"]);
            await BuildProcess.RunAsync("dotnet", ["build", Solution, "--configuration", "Release", "--no-restore"]);
            string testResults = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/repository");
            if (Directory.Exists(testResults))
            {
                Directory.Delete(testResults, recursive: true);
            }
            if (args.Length == 2)
            {
                await CodeCoverageVerification.RunTestsAsync(testProjects);
            }
            else
            {
                Console.WriteLine($"Running {testProjects.Length} Docker-free test projects.");
                await RunTestsAsync(testProjects, testResults, collectCoverage: false);
            }
            CopyLifecycleReports();
            int cases = await ValidationVerification.CompareWithGoAsync();
            await ResourceVerification.CompareWithGoAsync();
            await PreviewHelperVerification.CompareWithGoAsync();
            await SnapshotCacheVerification.CompareWithGoAsync();
            await SotwSubscriptionVerification.CompareWithGoAsync();
            await DeltaSubscriptionVerification.CompareWithGoAsync();
            await DeltaCacheVerification.CompareWithGoAsync();
            await LinearCacheVerification.CompareWithGoAsync();
            await MuxCacheVerification.CompareWithGoAsync();
            await HeartbeatVerification.CompareWithGoAsync();
            await FetchVerification.CompareWithGoAsync();
            await AdsClientVerification.CompareWithGoAsync();
            await ConversionVerification.CompareWithGoAsync();
            await DeltaStreamVerification.CompareWithGoAsync();
            await SotwStreamVerification.CompareWithGoAsync();
            await RootDifferentialVerification.VerifyAsync();
            // Solution packing intentionally traverses the non-packable examples, probes and tests.
            await BuildProcess.RunAsync("dotnet", ["pack", Solution, "--configuration", "Release", "--no-build", "--no-restore",
                "-p:WarnOnPackingNonPackableProject=false", "--output", BindingPaths.PackageDirectory]);
            PackageContentsVerification.Verify();
            await PackageVerification.VerifyCleanConsumerAsync();
            await PackageVerification.VerifyCleanConsumerAsync(ValidationPaths.TestsProject, "validation-consumer");
            await PackageVerification.VerifyCleanConsumerAsync("tests/Envoy.ControlPlane.Tests/Envoy.ControlPlane.Tests.csproj", "core-consumer");
            await PackageVerification.VerifyCleanConsumerAsync("tests/Envoy.ControlPlane.AspNetCore.Tests/Envoy.ControlPlane.AspNetCore.Tests.csproj", "hosting-consumer");
            await RootConsumerVerification.RunAsync("Core");
            await RootConsumerVerification.RunAsync("AspNetCore");
            await ReproducibilityReport.WriteAsync(Path.Combine(BindingPaths.InputsDirectory, "results", "reproducibility", "snapshot.json"));
            Console.WriteLine($"Repository build, tests, packages, consumers and reproducibility report passed; {cases} whole-message Go PGV cases, declared field-rule coverage and the snapshot/SotW-and-Delta-cache/LinearCache/MuxCache/heartbeat/Fetch/HTTP-gateway/ADS-client/conversion/helper/subscription/stream corpora matched. Released-Envoy verification remains a separate command.");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal static string[] DiscoverDockerFreeTestProjects(string solution)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(solution))!;
        return XDocument.Load(solution).Descendants("Project")
            .Select(project => Path.Combine(directory, project.Attribute("Path")!.Value.Replace('\\', '/')))
            .Where(path =>
            {
                var properties = XDocument.Load(path).Root!.Elements("PropertyGroup").Elements().ToArray();
                return IsEnabled(properties, "IsTestProject") && !IsEnabled(properties, "RequiresDocker");
            })
            .ToArray();
    }

    private static bool IsEnabled(XElement[] properties, string name) =>
        bool.TryParse(properties.LastOrDefault(property => property.Name == name)?.Value, out bool enabled) && enabled;

    internal static async Task RunTestsAsync(IReadOnlyList<string> projects, string results, bool collectCoverage)
    {
        foreach (string project in projects)
        {
            string projectResults = Path.Combine(results, Path.GetFileNameWithoutExtension(project));
            string[] arguments = ["test", "--project", project, "--configuration", "Release", "--no-build", "--no-restore",
                "--results-directory", projectResults, "--report-xunit-trx"];
            await BuildProcess.RunAsync("dotnet", collectCoverage ? [.. arguments, "--coverlet"] : arguments);
            if (collectCoverage)
            {
                Require(Directory.EnumerateFiles(projectResults, "*.xml", SearchOption.AllDirectories).Any(),
                    "No coverage report was produced for " + project);
            }
        }
    }

    private static void CopyLifecycleReports()
    {
        string output = Path.Combine(BindingPaths.InputsDirectory, "results", "lifecycle");
        Directory.CreateDirectory(output);
        foreach (string name in new[] { "sotw-stress-ordered.json", "sotw-stress-unordered.json", "delta-stress.json" })
        {
            string source = Path.Combine("tests", "Envoy.ControlPlane.AspNetCore.Tests", "bin", "Release", "net10.0", name);
            Require(File.Exists(source), "Missing discovery lifecycle report: " + source);
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
