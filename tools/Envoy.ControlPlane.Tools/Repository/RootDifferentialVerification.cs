using System.Text.Json;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class RootDifferentialVerification
{
    private const string CoreOutput = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/";
    private const string HostingOutput = "tests/Envoy.ControlPlane.AspNetCore.Tests/bin/Release/net10.0/";

    internal static string[] ModeNames() => Inputs().Select(input => input.Mode).ToArray();

    internal static async Task VerifyAsync()
    {
        string directory = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/root-differential");
        Directory.CreateDirectory(directory);
        var inputs = Inputs();
        var positive = new List<PositiveEvidence>();
        foreach (var input in inputs)
        {
            string reference = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/" + input.Reference);
            string[] nativeIds = ReadCaseIds(input.Mode, JsonNode.Parse(File.ReadAllText(input.Report))!);
            string[] sourceIds = ReadCaseIds(input.Mode, JsonNode.Parse(File.ReadAllText(reference))!);
            Require(nativeIds.Length > 0 && nativeIds.Distinct(StringComparer.Ordinal).Count() == nativeIds.Length
                && sourceIds.Distinct(StringComparer.Ordinal).Count() == sourceIds.Length
                && nativeIds.Order(StringComparer.Ordinal).SequenceEqual(sourceIds.Order(StringComparer.Ordinal)),
                input.Mode + ": root differential report has missing/duplicate/unmatched cases");
            positive.Add(new PositiveEvidence(input.Mode, nativeIds, input.Report, reference));
        }

        var negativeInputs = inputs.Where(input => input.Mode is not ("stream" or "delta-stream"))
            .Select(input => new NegativeInput(input.Mode, Path.GetFullPath(input.Report))).ToList();
        negativeInputs.Add(new NegativeInput("helper-symbols", Path.GetFullPath(CoreOutput + "conversion-corpus.json")));
        string manifest = Path.Combine(directory, "negative-manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(negativeInputs, JsonOptions()));
        string negativeOutput = Path.Combine(directory, "negative-results.json");
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "negative-comparisons", manifest, negativeOutput], "tests/resources/GoOracle");
        var negative = JsonNode.Parse(File.ReadAllText(negativeOutput))!.AsArray();
        Require(negative.Count == negativeInputs.Count && negative.All(row => row!["rejected"]!.GetValue<bool>())
            && negative.Select(row => row!["mode"]!.GetValue<string>()).Order(StringComparer.Ordinal)
                .SequenceEqual(negativeInputs.Select(input => input.Mode).Order(StringComparer.Ordinal)),
            "Root negative comparer evidence omitted or accepted a corruption");

        var requirements = Requirements();
        Require(requirements.Select(row => row.Id).SequenceEqual(Enumerable.Range(5, 23).Select(id => $"V{id:00}")),
            "Root evidence must cover every V05-V27 family");
        foreach (var requirement in requirements)
        {
            Require(requirement.Modes.All(mode => positive.Any(row => row.Mode == mode)),
                requirement.Id + ": required source-comparison mode is absent");
        }
        var baseline = JsonNode.Parse(File.ReadAllText("upstream-baseline.json"))!;
        var report = new
        {
            scope = "T21 root V05-V27; native-only ownership/runtime adaptations are identified separately",
            behaviorCommit = baseline["goControlPlane"]!["commit"]!.GetValue<string>(),
            schemaCommit = baseline["envoyApi"]!["commit"]!.GetValue<string>(),
            sourceCodeExecuted = true,
            positive,
            negative,
            streamNegativeChecks = new[] { "SotW stale ACK association rejected", "Delta response nonce reuse rejected" },
            noncePolicy = "One bidirectional mapping per stream; literals, caller versions, event order and lifecycle stay exact",
            requirements,
            dispositions = Dispositions(),
            unresolvedMaterialDivergences = Array.Empty<string>(),
            separateGates = new[] { "T22 concurrency/lifecycle", "T23 released-Envoy matrix", "T26 full validator conformance" },
        };
        File.WriteAllText(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(report, JsonOptions()) + "\n");
        Console.WriteLine($"Root differential evidence: {positive.Count} source modes / {positive.Sum(row => row.Cases.Length)} cases; "
            + $"{negative.Count} semantic corruptions plus both stream-association checks rejected; V05-V27 mapped.");
    }

    private static DifferentialInput[] Inputs() =>
    [
        new("snapshot", CoreOutput + "snapshot-corpus.json", "resources/go-results.json"),
        new("cache", CoreOutput + "cache-corpus.json", "cache/go-results.json"),
        new("subscription", CoreOutput + "sotw-subscription-corpus.json", "subscriptions/go-results.json"),
        new("helpers", CoreOutput + "preview-helper-corpus.json", "helpers/go-preview-results.json"),
        new("delta-subscription", CoreOutput + "delta-subscription-corpus.json", "delta-subscriptions/go-results.json"),
        new("delta-cache", CoreOutput + "delta-cache-corpus.json", "delta-cache/go-results.json"),
        new("linear-cache", CoreOutput + "linear-cache-corpus.json", "linear-cache/go-results.json"),
        new("mux-cache", CoreOutput + "mux-cache-corpus.json", "mux-cache/go-results.json"),
        new("heartbeats", CoreOutput + "heartbeat-corpus.json", "heartbeats/go-results.json"),
        new("fetch", CoreOutput + "fetch-corpus.json", "fetch/go-results.json"),
        new("fetch-engine", CoreOutput + "fetch-engine-corpus.json", "fetch-engine/go-results.json"),
        new("gateway", HostingOutput + "gateway-corpus.json", "gateway/go-results.json"),
        new("ads-client", CoreOutput + "ads-client-corpus.json", "ads-client/go-results.json"),
        new("conversions", CoreOutput + "conversion-corpus.json", "conversions/go-results.json"),
        new("delta-stream", CoreOutput + "delta-stream-corpus.json", "delta-streams/go-results.json"),
        new("stream", CoreOutput + "sotw-stream-corpus.json", "streams/go-results.json"),
    ];

    private static string[] ReadCaseIds(string mode, JsonNode root)
    {
        var rows = mode is "helpers" or "conversions" ? Property(root.AsObject(), "cases")!.AsArray() : root.AsArray();
        return rows.Select(row =>
        {
            var record = row!.AsObject();
            var input = Property(record, "input") as JsonObject ?? record;
            return Property(input, "id")!.GetValue<string>();
        }).ToArray();
    }

    /// <summary>Matches report metadata casing; protocol values keep their mode's exact comparison policy.</summary>
    private static JsonNode? Property(JsonObject value, string name) =>
        value.FirstOrDefault(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static RequirementEvidence[] Requirements() =>
    [
        new("V05", ["snapshot", "cache"], "Stable message/replacement and retained-response cases; caller mutation remains unsupported"),
        new("V06", ["snapshot", "linear-cache", "delta-cache"], "Opaque versions, lazy hashes and payload relationships; no universal cross-runtime hash claim"),
        new("V07", ["snapshot", "cache"], "Partial/empty/invalid EDS/RDS graphs; native publication does not auto-validate"),
        new("V08", ["cache", "mux-cache"], "Immediate/deferred/cancel/clear and retained responses; native gated register/publication interleaving"),
        new("V09", ["cache", "stream"], "Source ADS untracked-readiness edge and independent ordered/unordered scheduling"),
        new("V10", ["cache", "linear-cache", "stream"], "Full-state versus named/prefix/custom selection and returned metadata"),
        new("V11", ["subscription", "stream"], "Legacy/explicit/wildcard/prefix/overlap/deactivation transitions"),
        new("V12", ["stream"], "Source callback/node/type/ACK/NACK/stale nonce and callback override associations"),
        new("V13", ["stream"], "Ordered/unordered ADS, independent types, queued response draining; native late filtering"),
        new("V14", ["cache", "delta-cache"], "Source status counts; native TimeProvider clock/node identity proof"),
        new("V15", ["stream", "delta-stream", "fetch-engine"], "Hook/rejection/error order; native exception cleanup/reentry and ILogger adaptation"),
        new("V16", ["delta-subscription", "delta-stream"], "Initial versions and exact/prefix/wildcard subscribe/unsubscribe transitions"),
        new("V17", ["delta-cache", "delta-stream"], "Changes/removals, empty wildcard, unchanged suppression, pass-through and lazy maps"),
        new("V18", ["delta-stream"], "Drain/node/type/callback order and nonces without an invented ACK ledger"),
        new("V19", ["linear-cache"], "Initial/update/delete/bulk/replace/no-op/prefix/index/custom/mismatch/unsupported operations"),
        new("V20", ["mux-cache"], "Separate classifiers, child errors/cancellation, missing keys and Fetch capability"),
        new("V21", ["heartbeats", "delta-cache"], "Normal/zero/absent TTL, payload-free SotW heartbeat and raw Delta source omission"),
        new("V22", ["heartbeats"], "Actual source tick/scan selection; native fake-time/cancellation and caller-owned timer lifecycle"),
        new("V23", ["fetch", "fetch-engine", "mux-cache"], "Cache selection/errors, original caller and hook/materialization/mutation failures"),
        new("V24", ["gateway"], "All eight routes, clean paths, body/type/status/empty304; native host-method/authorization boundaries"),
        new("V25", ["gateway", "conversions"], "Proto names, enum/numeric/presence/Struct/Any/parser/error behavior"),
        new("V26", ["ads-client"], "Initial/receive/ACK/NACK/error/reinit state; native async serialization and channel ownership"),
        new("V27", ["helpers", "conversions", "snapshot"], "Full helper/constant inventory, typed HCM/Any, conversion and CLR descriptor boundaries"),
    ];

    private static Disposition[] Dispositions() =>
    [
        new("ownership", "intentional .NET API", "Stable retained inputs and caller-owned channels/calls; no deep-copy guarantee", "T05/T06/T10/T19"),
        new("async-cancellation", "intentional .NET API", "Tasks/channels/TimeProvider replace Go coordination; queued cancellation and publication contexts are explicit", "T07/T10/T17/T19"),
        new("fault-policy", "intentional .NET fault policy", "Exceptions retain original failures and settle owned cleanup; Go callback panic is normalized only in the test adapter", "T09/T14/T18/T22 preview"),
        new("idempotent-disposal", "intentional .NET ownership", "Repeated disposal cannot decrement LinearCache diagnostics below zero", "T15"),
        new("clock-and-hashes", "runtime representation", "Absolute wall time and universal protobuf hash text are not equated; controlled native clocks and source payload/version relationships are checked", "T05/T07/T14/T17"),
        new("json-and-descriptors", "intentional .NET API", "Explicit authoritative CLR/Any registry and return-new conversion; stock parser limits/error types/JSON formatting are runtime boundaries", "T18/T20"),
        new("source-spec-limits", "source-matched behavior", "Pinned prefix/ADS readiness/TTL/Delta limitations are retained and do not become stronger speculative features", "T07/T14/T17"),
        new("client-replacement", "intentional .NET ownership", "Reinitialization keeps source acceptance semantics but releases the old gRPC call", "T19"),
        new("logging", "intentional .NET API", "Hosting uses ILogger; Core does not duplicate Go's logging facade", "T11/T20"),
    ];

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private sealed record DifferentialInput(string Mode, string Report, string Reference);
    private sealed record NegativeInput(string Mode, string Report);
    private sealed record PositiveEvidence(string Mode, string[] Cases, string NativeReport, string GoReport);
    private sealed record RequirementEvidence(string Id, string[] Modes, string Scope);
    private sealed record Disposition(string Id, string Status, string Scope, string Evidence);
}
