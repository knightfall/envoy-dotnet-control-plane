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
            int cases = await ValidationVerification.CompareWithGoAsync();
            await ResourceVerification.CompareWithGoAsync();
            await SnapshotCacheVerification.CompareWithGoAsync();
            await SotwSubscriptionVerification.CompareWithGoAsync();
            await SotwStreamVerification.CompareWithGoAsync();
            await BuildProcess.RunAsync("dotnet", ["pack", Solution, "--configuration", "Release", "--no-build", "--no-restore",
                "--output", BindingPaths.PackageDirectory]);
            PackageContentsVerification.Verify();
            await PackageVerification.VerifyCleanConsumerAsync();
            await PackageVerification.VerifyCleanConsumerAsync(ValidationPaths.TestsProject, "validation-consumer");
            await PackageVerification.VerifyCleanConsumerAsync("tests/Envoy.ControlPlane.Tests/Envoy.ControlPlane.Tests.csproj", "core-consumer");
            await RootConsumerVerification.RunAsync("Core");
            await RootConsumerVerification.RunAsync("AspNetCore");
            Console.WriteLine($"Repository build, tests, packages and consumers passed; {cases} Go PGV cases and the snapshot/cache/subscription/stream corpora matched. Network adapters, Delta delivery and full GV remain separate tasks.");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void VerifyProjectInventory()
    {
        var registered = XDocument.Load(Solution).Descendants("Project")
            .Select(project => project.Attribute("Path")!.Value.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        var maintained = new[] { "src", "tests", "tools" }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Select(path => path.Replace('\\', '/'))
            .Where(path => !path.StartsWith("tests/validation/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        Require(registered.SetEquals(maintained), "Solution project inventory differs from maintained source projects");
        Console.WriteLine($"Solution includes all {registered.Count} maintained projects; historical provider probes are excluded.");
    }
}
