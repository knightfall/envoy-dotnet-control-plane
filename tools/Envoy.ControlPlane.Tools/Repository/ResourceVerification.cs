using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ResourceVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string directory = "tests/resources/GoOracle";
        string revision = JsonNode.Parse(File.ReadAllText("upstream-baseline.json"))!["goControlPlane"]!["commit"]!.GetValue<string>();
        foreach (string module in new[] { "github.com/envoyproxy/go-control-plane", "github.com/envoyproxy/go-control-plane/envoy" })
        {
            string metadata = await BuildProcess.RunAsync("go", ["list", "-mod=readonly", "-m", "-json", module], directory);
            var resolved = JsonNode.Parse(metadata)!;
            Require(resolved["Replace"] is null && resolved["Version"]!.GetValue<string>().EndsWith("-" + revision[..12], StringComparison.Ordinal),
                "Snapshot oracle module differs from the pinned Go baseline: " + module);
        }

        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/snapshot-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        var fixtures = JsonNode.Parse(File.ReadAllText("tests/Envoy.ControlPlane.Tests/Fixtures/snapshot-cases.json"))!.AsArray();
        Require(rows.Select(row => row!["Id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "Snapshot report omitted or duplicated a declared fixture");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/resources/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", Path.GetFullPath(report), output], directory);
    }
}
