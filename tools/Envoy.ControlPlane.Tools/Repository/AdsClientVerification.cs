using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class AdsClientVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/ads-client-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        Require(rows.Count == 12 && rows.Select(row => row!["input"]!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == 12,
            "ADS client report omitted or duplicated a case");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/ads-client/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "ads-client", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
