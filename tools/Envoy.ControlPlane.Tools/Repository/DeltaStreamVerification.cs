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
        var references = JsonNode.Parse(File.ReadAllText(output))!.AsArray();
        Require(references.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(rows.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "Go Delta report omitted or duplicated a declared trace");
        foreach (var row in rows)
        {
            string id = row!["id"]!.GetValue<string>();
            var reference = references.Single(item => item!["id"]!.GetValue<string>() == id)!;
            DiscoveryTraceComparer.CompareDelta(id, row["observed"]!.AsObject(), reference["observed"]!.AsObject());
        }
        VerifyNonceAssociationIsRejected(rows, references);
        Console.WriteLine($"Delta differential comparison: {rows.Count} traces matched; injected nonce reuse was rejected.");
    }

    private static void VerifyNonceAssociationIsRejected(JsonArray rows, JsonArray references)
    {
        var row = rows.First(item => item!["observed"]!["events"]!.AsArray().Count(value =>
            value!.GetValue<string>().StartsWith("response|", StringComparison.Ordinal)) >= 2)!;
        string id = row["id"]!.GetValue<string>();
        var candidate = row["observed"]!.DeepClone().AsObject();
        var events = candidate["events"]!.AsArray();
        int[] responses = Enumerable.Range(0, events.Count).Where(index =>
            events[index]!.GetValue<string>().StartsWith("response|", StringComparison.Ordinal)).ToArray();
        string nonce = events[responses[0]]!.GetValue<string>().Split('|')[3];
        string[] changed = events[responses[1]]!.GetValue<string>().Split('|');
        changed[3] = nonce;
        events[responses[1]] = string.Join('|', changed);
        var reference = references.Single(item => item!["id"]!.GetValue<string>() == id)!["observed"]!.AsObject();
        try
        {
            DiscoveryTraceComparer.CompareDelta(id, candidate, reference);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("Delta comparer accepted a reused nonce across responses");
    }
}
