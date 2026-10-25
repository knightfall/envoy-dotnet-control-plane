using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class SotwSubscriptionVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/sotw-subscription-corpus.json";
        var rows = JsonNode.Parse(File.ReadAllText(report))!.AsArray();
        var fixtures = JsonNode.Parse(File.ReadAllText("tests/Envoy.ControlPlane.Tests/Fixtures/sotw-subscription-cases.json"))!.AsArray();
        Require(rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "SotW report omitted or duplicated a declared transition trace");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/subscriptions/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "subscription", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
