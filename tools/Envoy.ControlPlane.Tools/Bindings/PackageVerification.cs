using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingPaths;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class PackageVerification
{
    internal static async Task BuildPackageAsync()
    {
        await PackAsync(ValidationPaths.AnnotationsProject);
        await PackAsync(ProtocolProject);
    }

    internal static async Task PackAsync(string project)
    {
        await BuildProcess.RunAsync("dotnet", ["restore", project, "--locked-mode"]);
        await BuildProcess.RunAsync("dotnet", ["pack", project, "--configuration", "Release", "--no-restore",
            "--output", PackageDirectory]);
    }

    internal static async Task RunTestsAsync(string project, string reportName)
    {
        await BuildProcess.RunAsync("dotnet", ["restore", project, "--locked-mode"]);
        await BuildProcess.RunAsync("dotnet", ["test", "--project", project, "--configuration", "Release", "--no-restore",
            "--results-directory", Path.GetFullPath(InputsDirectory + "/results/" + reportName),
            "--report-xunit-trx", "--report-xunit-trx-filename", reportName + ".trx"]);
    }

    internal static async Task VerifyCleanConsumerAsync(string testProject = ProtocolTests, string reportName = "consumer")
    {
        string repository = Path.GetFullPath(".");
        string scratch = Path.Combine(Path.GetTempPath(), "envoy-protocol-consumer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        try
        {
            foreach (string name in new[] { "global.json", "Directory.Build.props", "Directory.Packages.props", ".editorconfig" })
            {
                File.Copy(name, Path.Combine(scratch, name));
            }
            CopyTestSources(testProject, scratch);

            string consumer = CreateConsumerProject(scratch, testProject);
            WriteConsumerPackageSources(scratch, repository);

            // Keep the fresh cache under obj so SDK source globs exclude package content.
            string packageCache = Path.Combine(scratch, "obj", "packages");
            await BuildProcess.RunAsync("dotnet", ["restore", consumer, "--packages", packageCache], scratch);
            await BuildProcess.RunAsync("dotnet", ["restore", consumer, "--locked-mode", "--packages", packageCache], scratch);
            await BuildProcess.RunAsync("dotnet", ["test", "--project", consumer, "--configuration", "Release", "--no-restore",
                "--results-directory", Path.Combine(repository, InputsDirectory, "results", reportName),
                "--report-xunit-trx", "--report-xunit-trx-filename", reportName + ".trx"], scratch);

            File.Copy(Path.Combine(scratch, "packages.lock.json"),
                InputsDirectory + "/" + reportName + ".packages.lock.json", overwrite: true);
            File.Copy(Path.Combine(scratch, "packages.lock.json"),
                Path.Combine(repository, InputsDirectory, "results", reportName, "packages.lock.json"), overwrite: true);
            Console.WriteLine("Clean package consumer passed with no project reference and a fresh NuGet cache.");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static void CopyTestSources(string testProject, string destination)
    {
        string sourceDirectory = Path.GetDirectoryName(testProject)!;
        int fixtures = 0;
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => (Path.GetExtension(path) is ".cs" or ".json") && Path.GetFileName(path) != "packages.lock.json"))
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, source);
            if (relativePath.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            {
                continue;
            }

            string target = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
            if (Path.GetExtension(source) == ".json")
            {
                fixtures++;
            }
        }
        foreach (var source in XDocument.Load(testProject).Descendants("Compile").Where(item => item.Attribute("Include") is not null))
        {
            if (source.Attribute("Include")!.Value == ValidationConformance.GeneratedInclude)
            {
                ValidationConformance.CopyToConsumer(destination);
                continue;
            }
            string path = Path.Combine(sourceDirectory, source.Attribute("Include")!.Value);
            string target = Path.Combine(destination, source.Attribute("Link")?.Value ?? Path.GetFileName(path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
        Console.WriteLine($"Staged test sources, linked helpers and {fixtures} JSON fixtures from {testProject}.");
    }

    internal static string CreateConsumerProject(string directory, string testProject = ProtocolTests)
    {
        var project = XDocument.Load(testProject);
        foreach (var reference in project.Descendants("ProjectReference").ToArray())
        {
            string referencedProject = Path.Combine(Path.GetDirectoryName(testProject)!, reference.Attribute("Include")!.Value);
            var definition = XDocument.Load(referencedProject);
            string packageId = definition.Descendants("PackageId").SingleOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(referencedProject);
            reference.ReplaceWith(new XElement("PackageReference", new XAttribute("Include", packageId)));
        }
        project.Descendants("Compile").Where(item => item.Attribute("Include") is not null).Remove();
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
