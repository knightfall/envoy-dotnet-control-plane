using System.Text.Json;
using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Time.Testing;
using static Envoy.ControlPlane.Tests.Support.HeartbeatFixtures;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;
using DiscoveryResource = Envoy.Service.Discovery.V3.Resource;

namespace Envoy.ControlPlane.Tests.Support;

internal static class HeartbeatCorpus
{
    internal static HeartbeatInput[] Cases() =>
    [
        new("normal-ttl") { Normal = true, ExpectedNames = ["a"] },
        new("normal-zero-ttl") { Normal = true, Resources = [new("a", TtlNanoseconds: 0)], ExpectedNames = ["a"] },
        new("normal-without-ttl") { Normal = true, Resources = [new("a")], ExpectedNames = ["a"] },
        new("heartbeat-exact") { ExpectedNames = ["a"] },
        new("heartbeat-wildcard")
        {
            Wildcard = true, Names = [], Resources = [new("a", TtlNanoseconds: 5_000_000_000), new("plain")],
            Known = new() { ["a"] = "v1", ["plain"] = "v1" }, ExpectedNames = ["a"],
        },
        new("heartbeat-overlap") { Wildcard = true, ExpectedNames = ["a"] },
        new("heartbeat-prefix-only")
        {
            Names = [], Prefixes = ["collection/"], Resources = [new("collection/a", TtlNanoseconds: 5_000_000_000)],
            Known = new() { ["collection/a"] = "v1" }, ExpectedPending = 1,
        },
        new("heartbeat-exact-and-prefix")
        {
            Prefixes = ["collection/"], Resources = [new("a", TtlNanoseconds: 5_000_000_000), new("collection/a", TtlNanoseconds: 5_000_000_000)],
            ExpectedNames = ["a"],
        },
        new("heartbeat-without-ttl") { Resources = [new("a")], ExpectedPending = 1 },
        new("heartbeat-zero-ttl") { Resources = [new("a", TtlNanoseconds: 0)], ExpectedNames = ["a"] },
        new("heartbeat-missing-snapshot") { MissingSnapshot = true, ExpectedPending = 1 },
        new("heartbeat-missing-name") { Names = ["missing"], Known = new(), ExpectedPending = 1 },
        new("heartbeat-ads-untracked")
        {
            Ads = true, Resources = [new("a", TtlNanoseconds: 5_000_000_000), new("other", TtlNanoseconds: 5_000_000_000)],
        },
    ];

    internal static async Task<HeartbeatResult> EvaluateAsync(HeartbeatInput input)
    {
        var clock = new FakeTimeProvider();
        var cache = new SnapshotCache(input.Ads, timeProvider: clock);
        if (!input.MissingSnapshot)
        {
            await cache.SetSnapshotAsync("node", Routes("v1", input.Resources.Select(entry => Entry(entry.Name,
                entry.TtlNanoseconds is { } nanoseconds ? TimeSpan.FromTicks(nanoseconds / 100) : null)).ToArray()));
        }
        var channel = Channel.CreateBounded<ISotwResponse>(1);
        var subscription = Subscription(input.Names, input.Wildcard, input.Known, input.Prefixes);
        var registration = cache.CreateWatch(Request(ResourceTypeUrls.Route, input.Normal ? "" : "v1"), subscription, channel.Writer);
        try
        {
            if (!input.Normal)
            {
                await cache.SetSnapshotAsync("sentinel", Routes("v1", Entry("sentinel", TimeSpan.FromSeconds(5))));
                using var sentinel = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1", "sentinel"), Known("sentinel"));
                using var stop = new CancellationTokenSource();
                Task loop = cache.RunHeartbeatsAsync(Interval, new ResponseContext(stop.Token));
                try
                {
                    clock.Advance(Interval);
                    await Wait(sentinel.Completion);
                    _ = cache.GetStatusKeys();
                }
                finally
                {
                    stop.Cancel();
                    await Wait(loop);
                }
            }
            HeartbeatFrame? frame = null;
            if (channel.Reader.TryRead(out var response))
            {
                var envelope = response.GetDiscoveryResponse();
                frame = new HeartbeatFrame(envelope.VersionInfo, envelope.TypeUrl,
                    envelope.Resources.Select(Observe).OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray(),
                    response.ReturnedResources.ToDictionary(StringComparer.Ordinal));
            }
            return new HeartbeatResult(input, new HeartbeatObservation(frame, cache.GetStatusInfo("node")!.NumWatches,
                input.MissingSnapshot ? 0 : cache.GetSnapshot("node").GetResources(ResourceTypeUrls.Route)!.Count));
        }
        finally
        {
            await ((IAsyncDisposable)registration).DisposeAsync();
        }
    }

    private static HeartbeatResource Observe(Any resource)
    {
        if (resource.Is(DiscoveryResource.Descriptor))
        {
            var wrapper = resource.Unpack<DiscoveryResource>();
            return new HeartbeatResource(wrapper.Name, true, (wrapper.Ttl.Seconds * 1_000_000_000) + wrapper.Ttl.Nanos,
                wrapper.Resource_ is not null, wrapper.Resource_?.Value.ToBase64() ?? "", wrapper.Version);
        }
        return new HeartbeatResource(resource.Unpack<Config.Route.V3.RouteConfiguration>().Name, false, null, true, resource.Value.ToBase64(), "");
    }

    internal static void WriteResults(List<HeartbeatResult> results) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "heartbeat-corpus.json"),
        JsonSerializer.Serialize(results, DeltaCacheCorpus.JsonOptions) + "\n");
}

internal sealed record HeartbeatInput(string Id)
{
    public bool Normal { get; init; }
    public bool Wildcard { get; init; }
    public bool Ads { get; init; }
    public bool MissingSnapshot { get; init; }
    public string[] Names { get; init; } = ["a"];
    public string[] Prefixes { get; init; } = [];
    public Dictionary<string, string> Known { get; init; } = new() { ["a"] = "v1" };
    public DeltaCacheEntry[] Resources { get; init; } = [new("a", TtlNanoseconds: 5_000_000_000)];
    public string[] ExpectedNames { get; init; } = [];
    public int ExpectedPending { get; init; }
}

internal sealed record HeartbeatResource(string Name, bool Wrapped, long? TtlNanoseconds, bool HasPayload, string Data, string Version);
internal sealed record HeartbeatFrame(string Version, string TypeUrl, HeartbeatResource[] Resources, Dictionary<string, string> Returned);
internal sealed record HeartbeatObservation(HeartbeatFrame? Response, int Watches, int Resources);
internal sealed record HeartbeatResult(HeartbeatInput Input, HeartbeatObservation Observed);
