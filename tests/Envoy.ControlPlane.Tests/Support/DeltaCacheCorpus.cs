using System.Text.Json;
using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;

namespace Envoy.ControlPlane.Tests.Support;

internal static class DeltaCacheCorpus
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    internal static DeltaCacheInput[] Cases()
    {
        var one = State("v1", new DeltaCacheEntry("a"));
        var empty = State("v1");
        var known = new Dictionary<string, string> { ["a"] = Hash("a") };
        return
        [
            new("no-snapshot"),
            new("initial-empty-wildcard")
            {
                Before = empty, ExpectedReady = true, ExpectedVersion = "v1",
            },
            new("empty-with-nonce") { Before = empty, Nonce = "accepted" },
            new("delayed-empty-publication") { After = empty },
            new("unchanged-initial-version")
            {
                Before = one, Subscribed = ["a"], Known = known, Nonce = "accepted",
            },
            new("opaque-initial-version")
            {
                Before = one, Subscribed = ["a"], Known = new() { ["a"] = "caller/opaque" },
                ExpectedReady = true, ExpectedAdded = ["a"], ExpectedVersion = "v1",
            },
            new("missing-known-removal")
            {
                Before = one, Known = new() { ["old"] = "v0" },
                ExpectedReady = true, ExpectedAdded = ["a"], ExpectedRemoved = ["old"], ExpectedVersion = "v1",
            },
            new("unwatched-known-removal")
            {
                Before = State("v1", new DeltaCacheEntry("a"), new DeltaCacheEntry("other")),
                Subscribed = ["a"], Known = new() { ["a"] = Hash("a"), ["other"] = Hash("other") },
                ExpectedReady = true, ExpectedRemoved = ["other"], ExpectedVersion = "v1",
            },
            new("prefix-only")
            {
                Before = State("v1", new DeltaCacheEntry("collection/a")), Subscribed = ["collection/*"], Legacy = false,
            },
            new("prefix-known-removal")
            {
                Before = State("v1", new DeltaCacheEntry("collection/a")), Subscribed = ["collection/*"], Legacy = false,
                Known = new() { ["collection/a"] = Hash("collection/a") },
                ExpectedReady = true, ExpectedRemoved = ["collection/a"], ExpectedVersion = "v1",
            },
            new("wildcard-explicit-unsubscribe")
            {
                Before = one, Subscribed = ["*", "a"], Unsubscribed = ["a"], Known = known,
                ExpectedReady = true, ExpectedAdded = ["a"], ExpectedVersion = "v1",
            },
            new("ttl-omitted")
            {
                Before = State("v1", new DeltaCacheEntry("a", TtlNanoseconds: 30_000_000_000)), Subscribed = ["a"],
                ExpectedReady = true, ExpectedAdded = ["a"], ExpectedVersion = "v1",
            },
            new("system-version-only")
            {
                Before = one, After = State("v2", new DeltaCacheEntry("a")), Subscribed = ["a"], Known = known, Nonce = "accepted",
            },
            new("payload-change-same-system-version")
            {
                Before = one, After = State("v1", new DeltaCacheEntry("a", Changed: true)),
                Subscribed = ["a"], Known = known, Nonce = "accepted",
                ExpectedReady = true, ExpectedAdded = ["a"], ExpectedVersion = "v1",
            },
        ];
    }

    internal static async Task<DeltaCacheResult> EvaluateAsync(DeltaCacheInput input)
    {
        var cache = new SnapshotCache(ads: true);
        Snapshot? snapshot = null;
        if (input.Before is { } before)
        {
            snapshot = CreateSnapshot(before);
            await cache.SetSnapshotAsync("node", snapshot);
        }
        var request = new Service.Discovery.V3.DeltaDiscoveryRequest
        {
            Node = new Node { Id = "node" }, TypeUrl = ResourceTypeUrls.Route, ResponseNonce = input.Nonce,
        };
        request.ResourceNamesSubscribe.Add(input.Subscribed);
        request.ResourceNamesUnsubscribe.Add(input.Unsubscribed);
        request.InitialResourceVersions.Add(input.Known);
        var subscription = new DeltaSubscription(input.Subscribed, input.Unsubscribed, input.Known, input.Legacy);
        var channel = Channel.CreateBounded<IDeltaResponse>(1);
        var registration = cache.CreateDeltaWatch(request, subscription, channel.Writer);
        try
        {
            if (input.After is { } after)
            {
                snapshot = CreateSnapshot(after);
                await cache.SetSnapshotAsync("node", snapshot);
            }
            DeltaCacheFrame? frame = null;
            if (channel.Reader.TryRead(out var response))
            {
                var envelope = response.GetDeltaDiscoveryResponse();
                frame = new DeltaCacheFrame(envelope.SystemVersionInfo, envelope.TypeUrl,
                    envelope.Resources.Select(resource => new DeltaCacheResource(resource.Name, resource.Version, resource.Resource_.Value.ToBase64(), resource.Ttl is not null))
                        .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray(),
                    envelope.RemovedResources.Order(StringComparer.Ordinal).ToArray(),
                    response.ReturnedResources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
            }
            return new DeltaCacheResult(input, new DeltaCacheObservation(frame, cache.GetStatusInfo("node")!.NumDeltaWatches,
                snapshot?.GetVersionMap(ResourceTypeUrls.Route) is not null));
        }
        finally
        {
            await ((IAsyncDisposable)registration).DisposeAsync();
        }
    }

    internal static void WriteResults(DeltaCacheResult[] results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "delta-cache-corpus.json"),
        JsonSerializer.Serialize(results, JsonOptions) + "\n");

    private static DeltaCacheSnapshot State(string version, params DeltaCacheEntry[] resources) => new(version, resources);
    private static string Hash(string name) => ResourceSerialization.ComputeHash(ResourceSerialization.Serialize(new RouteConfiguration { Name = name }));

    private static Snapshot CreateSnapshot(DeltaCacheSnapshot input) => new(input.Version, new Dictionary<string, IEnumerable<ResourceEntry>>
    {
        [ResourceTypeUrls.Route] = input.Resources.Select(resource => new ResourceEntry(
            new RouteConfiguration { Name = resource.Name, IgnorePortInHostMatching = resource.Changed },
            resource.TtlNanoseconds is { } nanoseconds ? TimeSpan.FromTicks(nanoseconds / 100) : null)),
    });
}

internal sealed record DeltaCacheInput(string Id)
{
    public DeltaCacheSnapshot? Before { get; init; }
    public DeltaCacheSnapshot? After { get; init; }
    public string[] Subscribed { get; init; } = [];
    public string[] Unsubscribed { get; init; } = [];
    public Dictionary<string, string> Known { get; init; } = new(StringComparer.Ordinal);
    public bool Legacy { get; init; } = true;
    public string Nonce { get; init; } = "";
    public bool ExpectedReady { get; init; }
    public string[] ExpectedAdded { get; init; } = [];
    public string[] ExpectedRemoved { get; init; } = [];
    public string ExpectedVersion { get; init; } = "";
}

internal sealed record DeltaCacheEntry(string Name, bool Changed = false, long? TtlNanoseconds = null);
internal sealed record DeltaCacheSnapshot(string Version, DeltaCacheEntry[] Resources);
internal sealed record DeltaCacheResource(string Name, string Version, string Data, bool HasTtl);
internal sealed record DeltaCacheFrame(string Version, string TypeUrl, DeltaCacheResource[] Resources, string[] Removed, Dictionary<string, string> Returned);
internal sealed record DeltaCacheObservation(DeltaCacheFrame? Response, int Watches, bool VersionMapBuilt);
internal sealed record DeltaCacheResult(DeltaCacheInput Input, DeltaCacheObservation Observed);
