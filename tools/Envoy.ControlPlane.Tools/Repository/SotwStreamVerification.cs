using System.Text.Json;
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
        var references = JsonNode.Parse(File.ReadAllText(output))!.AsArray();
        Require(references.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "Go SotW stream report omitted or duplicated a declared trace");
        foreach (var row in rows)
        {
            string id = row!["id"]!.GetValue<string>();
            var reference = references.Single(item => item!["id"]!.GetValue<string>() == id)!;
            DiscoveryTraceComparer.Compare(id, row["observed"]!.AsObject(), reference["observed"]!.AsObject());
        }
        VerifyStaleAssociationIsRejected(rows, references);
        var baseline = JsonNode.Parse(File.ReadAllText("upstream-baseline.json"))!;
        var summary = new
        {
            behaviorCommit = baseline["goControlPlane"]!["commit"]!.GetValue<string>(),
            schemaCommit = baseline["envoyApi"]!["commit"]!.GetValue<string>(),
            nonceComparison = "stream-wide bijection; literal tokens and caller versions remain exact",
            matchedCases = rows.Select(row => row!["id"]!.GetValue<string>()).ToArray(),
            injectedStaleAssociationRejected = true,
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "comparison.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"SotW differential comparison: {rows.Count} traces matched; injected stale ACK association was rejected.");
    }

    private static void VerifyStaleAssociationIsRejected(JsonArray rows, JsonArray references)
    {
        const string id = "stale-and-current-watch-nonces";
        var candidate = rows.Single(row => row!["id"]!.GetValue<string>() == id)!["observed"]!.DeepClone().AsObject();
        var reference = references.Single(row => row!["id"]!.GetValue<string>() == id)!["observed"]!.AsObject();
        candidate["events"]![5] = "request|type/A|stale||1|ignored|false";
        try
        {
            DiscoveryTraceComparer.Compare(id, candidate, reference);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("SotW comparer accepted the injected stale ACK association");
    }
}
