using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class DeltaResubscriptionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RepeatedExplicitSubscription_ResendsOnlyRequestedResourceAndPlainAcksStayQuiet(bool useLinearCache, bool wildcard)
    {
        var cache = new RouteCache(useLinearCache);
        await cache.PublishAsync("v1", Route("requested"), Route("other"));
        await using var stream = new DeltaHarness();
        cache.Start(stream);
        stream.Input.Push(InitialRequest("requested", wildcard ? "*" : "other"));
        var initial = await stream.NextResponse();
        Assert.Equal(["other", "requested"], initial.Resources.Select(resource => resource.Name).Order(StringComparer.Ordinal));
        var expected = Assert.Single(initial.Resources, resource => resource.Name == "requested");
        _ = await stream.NextWatch();

        await AssertQuietAck(initial.Nonce);
        var repeated = await Resubscribe();
        var combined = await Resubscribe(repeated.Nonce);
        await AssertQuietAck(combined.Nonce);
        await stream.Finish();
        Assert.False(stream.Sent.Reader.TryRead(out _));

        async Task AssertQuietAck(string nonce)
        {
            stream.Input.Push(DeltaStreamFixtures.Request(ResourceTypeUrls.Route, nonce: nonce));
            _ = await stream.NextWatch();
            Assert.Equal(1, cache.PendingWatches);
        }

        async Task<DeltaDiscoveryResponse> Resubscribe(string nonce = "")
        {
            stream.Input.Push(DeltaStreamFixtures.Request(ResourceTypeUrls.Route, nonce: nonce, additions: ["requested", "requested"]));
            _ = await stream.NextWatch();
            Assert.Equal(0, cache.PendingWatches);
            var response = await stream.NextResponse();
            Assert.Equal(expected, Assert.Single(response.Resources));
            Assert.Empty(response.RemovedResources);
            return response;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialResourceVersions_OnReconnectSuppressUnchangedResourcesAndTrackUpdates(bool useLinearCache)
    {
        var cache = new RouteCache(useLinearCache);
        var unchanged = Route("unchanged");
        await cache.PublishAsync("v1", unchanged, Route("updated"), Route("removed"));
        await using var first = new DeltaHarness();
        cache.Start(first);
        first.Input.Push(InitialRequest("unchanged", "updated", "removed"));
        var baseline = await first.NextResponse();
        Assert.Equal(3, baseline.Resources.Count);
        await first.Finish();

        await using var reconnected = new DeltaHarness();
        cache.Start(reconnected);
        var request = InitialRequest("unchanged", "updated", "removed");
        foreach (var resource in baseline.Resources)
        {
            request.InitialResourceVersions.Add(resource.Name, resource.Version);
        }
        reconnected.Input.Push(request);
        _ = await reconnected.NextWatch();
        Assert.Equal(1, cache.PendingWatches);

        await cache.PublishAsync("v2", unchanged, Route("updated", 128));
        var response = await reconnected.NextResponse();
        Assert.Equal("1", response.Nonce);
        var update = Assert.Single(response.Resources);
        Assert.Equal("updated", update.Name);
        Assert.NotEqual(request.InitialResourceVersions["updated"], update.Version);
        Assert.Equal(["removed"], response.RemovedResources);
        await reconnected.Finish();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedExplicitSubscription_ReportsRemovalWhenPreviouslyReturnedResourceIsAbsent(bool useLinearCache)
    {
        var cache = new RouteCache(useLinearCache);
        await cache.PublishAsync("v1", Route("requested"));
        await using var stream = new DeltaHarness();
        cache.Start(stream);
        stream.Input.Push(InitialRequest("requested"));
        var initial = await stream.NextResponse();
        Assert.Equal("requested", Assert.Single(initial.Resources).Name);
        _ = await stream.NextWatch();

        await cache.PublishAsync("v2");
        stream.Input.Push(DeltaStreamFixtures.Request(ResourceTypeUrls.Route, nonce: initial.Nonce, additions: ["requested"]));
        _ = await stream.NextWatch();
        Assert.Equal(0, cache.PendingWatches);
        var response = await stream.NextResponse();
        Assert.Empty(response.Resources);
        Assert.Equal(["requested"], response.RemovedResources);
        await stream.Finish();
    }

    private static DeltaDiscoveryRequest InitialRequest(params string[] names)
    {
        var request = DeltaStreamFixtures.Request(ResourceTypeUrls.Route, additions: names);
        request.Node = new Node { Id = "node" };
        return request;
    }

    private static RouteConfiguration Route(string name, uint? bodySize = null) =>
        new() { Name = name, MaxDirectResponseBodySizeBytes = bodySize };

    private sealed class RouteCache(bool useLinearCache)
    {
        private readonly SnapshotCache snapshot = new();
        private readonly LinearCache linear = new(ResourceTypeUrls.Route);

        internal int PendingWatches => useLinearCache ? linear.NumCacheWatches : snapshot.GetStatusInfo("node")!.NumDeltaWatches;

        internal void Start(DeltaHarness stream)
        {
            IConfigWatcher cache = useLinearCache ? linear : snapshot;
            stream.Watcher.Create = cache.CreateDeltaWatch;
            stream.Start(ResourceTypeUrls.Route);
        }

        internal Task PublishAsync(string version, params RouteConfiguration[] routes) => useLinearCache
            ? linear.SetResourcesAsync(routes.ToDictionary(route => route.Name, route => (IMessage?)route, StringComparer.Ordinal))
            : snapshot.SetSnapshotAsync("node", new Snapshot(version, new Dictionary<string, IEnumerable<IMessage?>>
            {
                [ResourceTypeUrls.Route] = routes,
            }));
    }
}
