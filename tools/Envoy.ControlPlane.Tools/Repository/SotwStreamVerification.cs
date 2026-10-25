using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class SotwStreamVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/sotw-stream-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        var fixtures = JsonNode.Parse(File.ReadAllText("tests/Envoy.ControlPlane.Tests/Fixtures/sotw-stream-cases.json"))!.AsArray();
        Require(rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "SotW stream report omitted or duplicated a declared trace");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/streams/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "stream", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
