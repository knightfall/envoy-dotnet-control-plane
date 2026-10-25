using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class SnapshotCacheVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/cache-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        var fixtures = JsonNode.Parse(File.ReadAllText("tests/Envoy.ControlPlane.Tests/Fixtures/cache-cases.json"))!.AsArray();
        Require(rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "SnapshotCache report omitted or duplicated a declared event trace");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/cache/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // Resource verification checks both pinned modules before this shared oracle is invoked.
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "cache", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
