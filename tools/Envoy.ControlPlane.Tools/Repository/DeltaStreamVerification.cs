using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class DeltaStreamVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/delta-stream-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        var fixtures = JsonNode.Parse(File.ReadAllText("tests/Envoy.ControlPlane.Tests/Fixtures/delta-stream-cases.json"))!.AsArray();
        Require(rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "Delta stream report omitted or duplicated a declared trace");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/delta-streams/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "delta-stream", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
