using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class HeartbeatVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/heartbeat-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        Require(rows.Count == 13 && rows.Select(row => row!["input"]!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == 13,
            "Heartbeat report omitted or duplicated a case");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/heartbeats/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "heartbeats", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
