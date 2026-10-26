using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class CodeCoverageVerification
{
    internal static async Task RunTestsAsync(string solution)
    {
        string results = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/coverage");
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }
        Directory.CreateDirectory(results);
        string[] projects = XDocument.Load(solution).Descendants("Project")
            .Select(project => project.Attribute("Path")!.Value)
            .Where(path => XDocument.Load(path).Descendants("IsTestProject").Any(value => value.Value == "true"))
            .ToArray();
        Require(projects.Length > 0, "The solution has no declared test projects for coverage");
        foreach (string project in projects)
        {
            string projectResults = Path.Combine(results, "raw", Path.GetFileNameWithoutExtension(project));
            await BuildProcess.RunAsync("dotnet", ["test", "--project", project, "--configuration", "Release", "--no-build", "--no-restore",
                "--results-directory", projectResults, "--report-xunit-trx", "--coverlet", "--coverlet-output-format", "cobertura",
                "--coverlet-include", "[Envoy.ControlPlane*]*", "--coverlet-exclude-by-file", "**/obj/**"]);
            Require(Directory.EnumerateFiles(projectResults, "*.xml", SearchOption.AllDirectories).Any(),
                "No coverage report was produced for " + project);
        }
        await BuildProcess.RunAsync("dotnet", ["tool", "restore"]);
        string report = Path.Combine(results, "report");
        await BuildProcess.RunAsync("dotnet", ["tool", "run", "reportgenerator",
            "-reports:" + Path.Combine(results, "raw", "**", "*.xml"), "-targetdir:" + report,
            "-reporttypes:Html;Cobertura;MarkdownSummaryGithub", "-sourcedirs:" + Path.GetFullPath(".")]);
        string cobertura = Path.Combine(report, "Cobertura.xml");
        Require(File.Exists(cobertura), "ReportGenerator did not produce merged Cobertura coverage");
        var coverage = XDocument.Load(cobertura).Root!;
        Require(int.TryParse(coverage.Attribute("lines-valid")?.Value, out int valid) && valid > 0,
            "Merged coverage has no measured source lines");
        Console.WriteLine($"Coverage collected from {projects.Length} test projects: {coverage.Attribute("lines-covered")?.Value}/{valid} measured lines. Reports: {report}");
    }
}
