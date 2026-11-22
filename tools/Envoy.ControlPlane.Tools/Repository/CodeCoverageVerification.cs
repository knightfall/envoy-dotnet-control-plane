using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class CodeCoverageVerification
{
    internal static async Task RunTestsAsync(IReadOnlyList<string> projects)
    {
        string results = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/coverage");
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }
        Directory.CreateDirectory(results);
        await RepositoryVerification.RunTestsAsync(projects, Path.Combine(results, "raw"), collectCoverage: true);
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
        VerifyLibraryCoverage(coverage);
        Console.WriteLine($"Coverage collected from {projects.Count} Docker-free test projects: {coverage.Attribute("lines-covered")?.Value}/{valid} measured lines. Reports: {report}");
    }

    internal static void VerifyLibraryCoverage(XElement coverage)
    {
        var packages = coverage.Element("packages")?.Elements("package").ToArray() ?? [];
        foreach (string name in new[] { "Envoy.ControlPlane", "Envoy.ControlPlane.AspNetCore", "Envoy.ControlPlane.Validation" })
        {
            var package = packages.SingleOrDefault(item => (string?)item.Attribute("name") == name);
            Require(package?.Descendants("line").Any() == true,
                "Coverage did not measure product source in " + name);
        }
    }
}
