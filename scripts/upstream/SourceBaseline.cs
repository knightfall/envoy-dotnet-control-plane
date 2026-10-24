using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static class SourceBaseline
{
    internal static Dictionary<string, JsonNode> Validate(string repositoryRoot, JsonObject inputLock)
    {
        var sdk = ReadJsonObject(Path.Combine(repositoryRoot, "global.json"))["sdk"]!;
        var lockedSdk = inputLock["inspectionTools"]!["dotnet"]!;
        Require(GetString(sdk, "version") == GetString(lockedSdk, "version"), "SDK pin differs from lock");

        var baseline = ReadJsonObject(Path.Combine(repositoryRoot, "upstream-baseline.json"));
        var sources = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        foreach (var source in GetArray(inputLock, "sources").Select(node => node!))
        {
            string sourceId = GetString(source, "id");
            Require(sources.TryAdd(sourceId, source), "Duplicate source ID: " + sourceId);
        }

        (string Key, string SourceId)[] requiredOrigins =
        [
            ("goControlPlane", "go-control-plane"),
            ("envoyApi", "envoy"),
            ("ratelimitApi", "ratelimit"),
        ];

        foreach (var (key, sourceId) in requiredOrigins)
        {
            Require(sources.ContainsKey(sourceId), "Missing required source: " + sourceId);
            var source = sources[sourceId];
            Require(GetString(baseline[key]!, "commit") == GetString(source, "commit") &&
                    GetString(baseline[key]!, "repository") == GetString(source, "repository"),
                "Baseline/lock origin mismatch: " + sourceId);
        }

        return sources;
    }
}
