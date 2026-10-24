using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingPaths;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class PackageVerification
{
    internal static async Task BuildPackageAsync()
    {
        await BuildProcess.RunAsync("dotnet", ["restore", ProtocolProject, "--locked-mode"]);
        await BuildProcess.RunAsync("dotnet", ["pack", ProtocolProject, "--configuration", "Release", "--no-restore",
            "--output", PackageDirectory]);
    }

    internal static async Task RunTestsAsync(string project, string reportName)
    {
        await BuildProcess.RunAsync("dotnet", ["restore", project, "--locked-mode"]);
        await BuildProcess.RunAsync("dotnet", ["test", "--project", project, "--configuration", "Release", "--no-restore",
            "--results-directory", Path.GetFullPath(InputsDirectory + "/results/" + reportName),
            "--report-xunit-trx", "--report-xunit-trx-filename", reportName + ".trx"]);
    }

    internal static async Task VerifyCleanConsumerAsync()
    {
        string repository = Path.GetFullPath(".");
        string scratch = Path.Combine(Path.GetTempPath(), "envoy-protocol-consumer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        try
        {
            foreach (string name in new[] { "global.json", "Directory.Packages.props", ".editorconfig" })
            {
                File.Copy(name, Path.Combine(scratch, name));
            }
            CopyTestSources(scratch);

            string consumer = CreateConsumerProject(scratch);
            WriteConsumerPackageSources(scratch, repository);

            // Keep the fresh cache under obj so SDK source globs exclude package content.
            string packageCache = Path.Combine(scratch, "obj", "packages");
            await BuildProcess.RunAsync("dotnet", ["restore", consumer, "--packages", packageCache], scratch);
            await BuildProcess.RunAsync("dotnet", ["restore", consumer, "--locked-mode", "--packages", packageCache], scratch);
            await BuildProcess.RunAsync("dotnet", ["test", "--project", consumer, "--configuration", "Release", "--no-restore",
                "--results-directory", Path.Combine(repository, InputsDirectory, "results/consumer"),
                "--report-xunit-trx", "--report-xunit-trx-filename", "consumer.trx"], scratch);

            File.Copy(Path.Combine(scratch, "packages.lock.json"),
                InputsDirectory + "/consumer.packages.lock.json", overwrite: true);
            Console.WriteLine("Clean package consumer passed with no project reference and a fresh NuGet cache.");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static void CopyTestSources(string destination)
    {
        string sourceDirectory = Path.GetDirectoryName(ProtocolTests)!;
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, source);
            if (relativePath.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            {
                continue;
            }

            string target = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
    }

    internal static string CreateConsumerProject(string directory)
    {
        var project = XDocument.Load(ProtocolTests);
        var packageReference = new XElement("PackageReference", new XAttribute("Include", "Envoy.ControlPlane.Protocol"));
        project.Descendants("ProjectReference").Single().ReplaceWith(packageReference);
        string path = Path.Combine(directory, "Consumer.csproj");
        project.Save(path);
        return path;
    }

    internal static void WriteConsumerPackageSources(string directory, string repository)
    {
        var sources = new XElement("packageSources", new XElement("clear"));
        sources.Add(new XElement("add", new XAttribute("key", "candidate"),
            new XAttribute("value", Path.Combine(repository, PackageDirectory))));
        sources.Add(new XElement("add", new XAttribute("key", "nuget.org"),
            new XAttribute("value", "https://api.nuget.org/v3/index.json")));
        new XDocument(new XElement("configuration", sources)).Save(Path.Combine(directory, "NuGet.Config"));
    }
}
