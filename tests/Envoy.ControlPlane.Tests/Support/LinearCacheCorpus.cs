using System.Text.Json;
using System.Threading.Channels;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;

namespace Envoy.ControlPlane.Tests.Support;

internal static class LinearCacheCorpus
{
    internal static LinearCacheInput[] Cases()
    {
        var initial = new[] { new LinearCacheEntry("a") };
        var epoch = new Dictionary<string, string> { ["a"] = "0" };
        // Independent RouteConfiguration { name: "a" } wire bytes.
        string hash = ResourceSerialization.ComputeHash(new byte[] { 10, 1, 97 });
        var hashed = new Dictionary<string, string> { ["a"] = hash };
        return
        [
            new("sotw-initial-empty")
            {
                Version = "",
                ExpectedReady = true,
                ExpectedVersion = "0",
            },
            new("sotw-empty-current"),
            new("delta-initial-empty")
            {
                Delta = true,
                Nonce = "",
                ExpectedReady = true,
                ExpectedVersion = "0",
            },
            new("delta-empty-current")
            {
                Delta = true,
            },
            new("initial-resources")
            {
                Initial = initial,
                Version = "",
                ExpectedReady = true,
                ExpectedNames = ["a"],
                ExpectedVersion = "0",
            },
            new("current-system-version-still-discovers-resources")
            {
                Initial = initial,
                Version = "0",
                ExpectedReady = true,
                ExpectedNames = ["a"],
                ExpectedVersion = "0",
            },
            new("sotw-unchanged")
            {
                Initial = initial,
                Known = epoch,
            },
            new("delta-unchanged")
            {
                Delta = true,
                Initial = initial,
                Known = hashed,
            },
            new("sotw-identical-update")
            {
                Initial = initial,
                Known = epoch,
                Operation = new("update", initial),
                ExpectedReady = true,
                ExpectedNames = ["a"],
                ExpectedVersion = "1",
            },
            new("delta-identical-update")
            {
                Delta = true,
                Initial = initial,
                Known = hashed,
                Operation = new("update", initial),
            },
            new("delta-changed-update")
            {
                Delta = true,
                Initial = initial,
                Known = hashed,
                Operation = new("update", [new("a", Changed: true)]),
                ExpectedReady = true,
                ExpectedNames = ["a"],
                ExpectedVersion = "1",
            },
            new("sotw-partial-removal")
            {
                Initial = initial,
                Known = epoch,
                Operation = new("delete", Delete: ["a"]),
            },
            new("delta-removal")
            {
                Delta = true,
                Initial = initial,
                Known = hashed,
                Operation = new("delete", Delete: ["a"]),
                ExpectedReady = true,
                ExpectedRemoved = ["a"],
                ExpectedVersion = "1",
            },
            new("cds-full-state-removal")
            {
                TypeUrl = ResourceTypeUrls.Cluster,
                Initial = initial,
                Known = epoch,
                Operation = new("delete", Delete: ["a"]),
                ExpectedReady = true,
                ExpectedVersion = "1",
            },
            new("cds-full-state-update")
            {
                TypeUrl = ResourceTypeUrls.Cluster,
                Initial = initial,
                Known = epoch,
                Operation = new("update", [new("b")]),
                ExpectedReady = true,
                ExpectedNames = ["a", "b"],
                ExpectedVersion = "1",
            },
            new("partial-update-retains-known-metadata")
            {
                Initial = initial,
                Known = epoch,
                Operation = new("update", [new("b")]),
                ExpectedReady = true,
                ExpectedNames = ["b"],
                ExpectedVersion = "1",
            },
            new("prefix-and-exact-overlap")
            {
                Subscribed = ["collection/*", "collection/a"],
                Operation = new("bulk", [new("collection/a"), new("outside")]),
                ExpectedReady = true,
                ExpectedNames = ["collection/a"],
                ExpectedVersion = "1",
            },
            new("delta-prefix-selection")
            {
                Delta = true,
                Subscribed = ["collection/*"],
                Operation = new("update", [new("collection/a")]),
                ExpectedReady = true,
                ExpectedNames = ["collection/a"],
                ExpectedVersion = "1",
            },
            new("unrelated-exact-update")
            {
                Subscribed = ["a"],
                Operation = new("update", [new("b")]),
            },
            new("replace-all")
            {
                Delta = true,
                Initial = initial,
                Known = hashed,
                Operation = new("set", [new("b")]),
                ExpectedReady = true,
                ExpectedNames = ["b"],
                ExpectedRemoved = ["a"],
                ExpectedVersion = "1",
            },
            new("replace-identical-sotw")
            {
                Initial = initial,
                Known = epoch,
                Operation = new("set", initial),
                ExpectedReady = true,
                ExpectedNames = ["a"],
                ExpectedVersion = "1",
            },
            new("replace-identical-delta")
            {
                Delta = true,
                Initial = initial,
                Known = hashed,
                Operation = new("set", initial),
            },
            new("missing-delete-advances-counter")
            {
                Before = [new("delete", Delete: ["missing"])],
                Version = "",
                ExpectedReady = true,
                ExpectedVersion = "1",
            },
            new("empty-bulk-advances-counter")
            {
                Before = [new("bulk")],
                Version = "",
                ExpectedReady = true,
                ExpectedVersion = "1",
            },
            new("empty-replacement-advances-counter")
            {
                Before = [new("set")],
                Version = "",
                ExpectedReady = true,
                ExpectedVersion = "1",
            },
            new("bulk-delete-wins")
            {
                Delta = true,
                Initial = initial,
                Known = hashed,
                Operation = new("bulk", [new("a", Changed: true)], ["a"]),
                ExpectedReady = true,
                ExpectedRemoved = ["a"],
                ExpectedVersion = "1",
            },
            new("restart-prefix")
            {
                Prefix = "new/",
                Version = "old/8",
                ExpectedReady = true,
                ExpectedVersion = "new/0",
            },
            new("matching-prefix-is-not-parsed")
            {
                Prefix = "new/",
                Version = "new/not-a-number",
            },
            new("delta-unknown-version")
            {
                Delta = true,
                Initial = initial,
                Known = new() { ["a"] = "opaque" },
                ExpectedReady = true,
                ExpectedNames = ["a"],
                ExpectedVersion = "0",
            },
            new("delta-missing-and-unsubscribed")
            {
                Delta = true,
                Initial = [new("a"), new("b")],
                Subscribed = ["a"],
                Known = new() { ["a"] = hash, ["b"] = "old", ["gone"] = "old" },
                ExpectedReady = true,
                ExpectedRemoved = ["b", "gone"],
                ExpectedVersion = "0",
            },
            new("bulk-null-payload")
            {
                Delta = true,
                Operation = new("bulk", [new("a", Null: true)]),
                ExpectedReady = true,
                ExpectedNames = ["a"],
                ExpectedVersion = "1",
            },
        ];
    }

    internal static async Task<LinearCacheResult> EvaluateAsync(LinearCacheInput input)
    {
        var cache = new LinearCache(input.TypeUrl, MakeResources(input.Initial, input.TypeUrl), input.Prefix);
        foreach (var operation in input.Before)
        {
            await ApplyAsync(cache, operation, input.TypeUrl);
        }
        ISubscription subscription;
        var sotw = new SotwSubscription(input.Subscribed, allowLegacyWildcard: false);
        sotw.SetReturnedResources(input.Known);
        subscription = input.Delta ? new DeltaSubscription(input.Subscribed, initialResourceVersions: input.Known, allowLegacyWildcard: false) : sotw;
        var sotwResponses = Channel.CreateBounded<ISotwResponse>(1);
        var deltaResponses = Channel.CreateBounded<IDeltaResponse>(1);
        var registration = input.Delta
            ? cache.CreateDeltaWatch(new DeltaDiscoveryRequest { TypeUrl = input.TypeUrl, ResponseNonce = input.Nonce }, subscription, deltaResponses.Writer)
            : cache.CreateWatch(new DiscoveryRequest { TypeUrl = input.TypeUrl, VersionInfo = input.Version }, subscription, sotwResponses.Writer);
        try
        {
            int initialWatches = cache.NumCacheWatches;
            int initialMatching = cache.NumWatches(input.ProbeName);
            int initialPrefixes = cache.NumPrefixWatches;
            int initialWildcards = cache.NumWildcardWatches;
            if (input.Operation is { } operation)
            {
                await ApplyAsync(cache, operation, input.TypeUrl);
            }
            DeltaCacheFrame? frame = null;
            if (sotwResponses.Reader.TryRead(out var response))
            {
                var envelope = response.GetDiscoveryResponse();
                frame = new DeltaCacheFrame(envelope.VersionInfo, envelope.TypeUrl,
                    envelope.Resources.Select(resource => new DeltaCacheResource("", "", resource.Value.ToBase64(), false))
                        .OrderBy(resource => resource.Data, StringComparer.Ordinal).ToArray(), [],
                    response.ReturnedResources.ToDictionary(StringComparer.Ordinal));
            }
            if (deltaResponses.Reader.TryRead(out var delta))
            {
                var envelope = delta.GetDeltaDiscoveryResponse();
                frame = new DeltaCacheFrame(envelope.SystemVersionInfo, envelope.TypeUrl,
                    envelope.Resources.Select(resource => new DeltaCacheResource(resource.Name, resource.Version, resource.Resource_.Value.ToBase64(), resource.Ttl is not null))
                        .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray(),
                    envelope.RemovedResources.Order(StringComparer.Ordinal).ToArray(), delta.ReturnedResources.ToDictionary(StringComparer.Ordinal));
            }
            return new LinearCacheResult(input, new LinearCacheObservation(frame, initialWatches, initialMatching, initialPrefixes, initialWildcards,
                cache.NumCacheWatches, cache.NumResources, cache.GetResources().Keys.Order(StringComparer.Ordinal).ToArray()));
        }
        finally
        {
            await ((IAsyncDisposable)registration).DisposeAsync();
        }
    }

    internal static void WriteResults(List<LinearCacheResult> results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "linear-cache-corpus.json"),
        JsonSerializer.Serialize(results, DeltaCacheCorpus.JsonOptions) + "\n");

    private static Dictionary<string, IMessage?> MakeResources(LinearCacheEntry[] entries, string typeUrl) => entries.ToDictionary(entry => entry.Name,
        entry => MakeResource(entry, typeUrl), StringComparer.Ordinal);

    private static IMessage? MakeResource(LinearCacheEntry entry, string typeUrl)
    {
        if (entry.Null)
        {
            return null;
        }
        return typeUrl == ResourceTypeUrls.Cluster
            ? new Cluster { Name = entry.Name, AltStatName = entry.Changed ? "changed" : "" }
            : new RouteConfiguration { Name = entry.Name, IgnorePortInHostMatching = entry.Changed };
    }

    private static Task ApplyAsync(LinearCache cache, LinearCacheOperation operation, string typeUrl)
    {
        var updated = MakeResources(operation.Resources ?? [], typeUrl);
        return operation.Kind switch
        {
            "update" => cache.UpdateResourceAsync(updated.Single().Key, updated.Single().Value!),
            "delete" => cache.DeleteResourceAsync(operation.Delete!.Single()),
            "bulk" => cache.UpdateResourcesAsync(updated, operation.Delete ?? []),
            "set" => cache.SetResourcesAsync(updated),
            _ => throw new InvalidOperationException("Unknown LinearCache operation: " + operation.Kind),
        };
    }
}

internal sealed record LinearCacheEntry(string Name, bool Changed = false, bool Null = false);
internal sealed record LinearCacheOperation(string Kind, LinearCacheEntry[]? Resources = null, string[]? Delete = null);
internal sealed record LinearCacheInput(string Id)
{
    public bool Delta { get; init; }
    public string TypeUrl { get; init; } = ResourceTypeUrls.Route;
    public string Prefix { get; init; } = "";
    public string Version { get; init; } = "0";
    public string Nonce { get; init; } = "accepted";
    public LinearCacheEntry[] Initial { get; init; } = [];
    public LinearCacheOperation[] Before { get; init; } = [];
    public LinearCacheOperation? Operation { get; init; }
    public string[] Subscribed { get; init; } = ["*"];
    public Dictionary<string, string> Known { get; init; } = new(StringComparer.Ordinal);
    public string ProbeName { get; init; } = "collection/a";
    public bool ExpectedReady { get; init; }
    public string[] ExpectedNames { get; init; } = [];
    public string[] ExpectedRemoved { get; init; } = [];
    public string ExpectedVersion { get; init; } = "";
}

internal sealed record LinearCacheObservation(DeltaCacheFrame? Response, int InitialWatches, int InitialMatching, int InitialPrefixes,
    int InitialWildcards, int Watches, int Resources, string[] Names);
internal sealed record LinearCacheResult(LinearCacheInput Input, LinearCacheObservation Observed);
