using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class FetchVerification
{
    internal static async Task CompareWithGoAsync()
    {
        await CompareAsync("fetch", "Envoy.ControlPlane.Tests", 14);
        await CompareAsync("gateway", "Envoy.ControlPlane.AspNetCore.Tests", 23);
    }

    private static async Task CompareAsync(string mode, string project, int count)
    {
        string report = $"tests/{project}/bin/Release/net10.0/{mode}-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        Require(rows.Count == count && rows.Select(row => row!["input"]!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == count,
            $"{mode} report omitted or duplicated a case");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + $"/results/{mode}/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", mode, Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
