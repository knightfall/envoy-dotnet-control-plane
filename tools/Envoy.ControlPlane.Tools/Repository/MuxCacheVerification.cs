using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class MuxCacheVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/mux-cache-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        Require(rows.Count == 12 && rows.Select(row => row!["input"]!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == 12,
            "MuxCache report omitted or duplicated a case");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/mux-cache/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "mux-cache", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
