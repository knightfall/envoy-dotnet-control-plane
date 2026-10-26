using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class DeltaSubscriptionVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/delta-subscription-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        var fixtures = JsonNode.Parse(File.ReadAllText("tests/Envoy.ControlPlane.Tests/Fixtures/delta-subscription-cases.json"))!.AsArray();
        Require(rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "Delta subscription report omitted or duplicated a declared trace");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/delta-subscriptions/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "delta-subscription", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
