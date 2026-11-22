using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotDeltaFanoutTests
{
    [Fact]
    public async Task WildcardFanout_SharesVersionsAndBytesWhileEachResponseOwnsItsEnvelope()
    {
        var initial = Routes("v1", Route("retained"), Route("updated"), Route("removed"));
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", initial);
        var initialRequest = DeltaRequest();
        using var firstInitial = cache.CreateDeltaWatch(initialRequest, new DeltaSubscription());
        using var secondInitial = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription());
        var known = (await Wait(firstInitial.Completion)).ReturnedResources;
        Assert.Same(known, (await Wait(secondInitial.Completion)).ReturnedResources);

        using var first = cache.CreateDeltaWatch(DeltaRequest("ack-1"), new DeltaSubscription(initialResourceVersions: known));
        using var second = cache.CreateDeltaWatch(DeltaRequest("ack-2"), new DeltaSubscription(initialResourceVersions: known));
        var updated = Route("updated", true);
        var observed = new ObservedResource(updated, () => { });
        var next = Routes("v2", observed, Route("retained"));
        await cache.SetSnapshotAsync("node", next);

        var responses = await Wait(Task.WhenAll(first.Completion, second.Completion));
        var versions = next.GetVersionMap(ResourceTypeUrls.Route);
        Assert.NotNull(versions);
        Assert.Equal(2, versions.Count);
        Assert.Equal(known["retained"], versions["retained"]);
        Assert.NotEqual(known["updated"], versions["updated"]);
        Assert.True(known.ContainsKey("removed"));
        Assert.False(versions.ContainsKey("removed"));
        Assert.All(responses, response => Assert.Same(versions, response.ReturnedResources));
        var a = responses[0].GetDeltaDiscoveryResponse();
        var b = responses[1].GetDeltaDiscoveryResponse();
        Assert.NotSame(a, b);
        Assert.Equal("removed", Assert.Single(a.RemovedResources));
        Assert.Equal("removed", Assert.Single(b.RemovedResources));
        var aResource = Assert.Single(a.Resources);
        var bResource = Assert.Single(b.Resources);
        Assert.NotSame(aResource, bResource);
        Assert.NotSame(aResource.Resource_, bResource.Resource_);
        Assert.Same(aResource.Resource_.Value, bResource.Resource_.Value);
        Assert.Equal(updated, bResource.Resource_.Unpack<RouteConfiguration>());
        Assert.Equal(versions["updated"], bResource.Version);
        Assert.Equal(1, observed.Attempts);

        a.Nonce = "nonce-a";
        aResource.Resource_.Value = ByteString.Empty;
        a.RemovedResources.Clear();
        Assert.Empty(b.Nonce);
        Assert.Equal(updated, bResource.Resource_.Unpack<RouteConfiguration>());
        Assert.Equal("removed", Assert.Single(b.RemovedResources));
    }

    [Fact]
    public async Task StreamWildcardUnsubscribe_CopiesVersionsUntilItsSentResponseIsCommitted()
    {
        var cache = new SnapshotCache();
        var snapshot = Routes("v1", Route("route"), Route("other"));
        await cache.SetSnapshotAsync("node", snapshot);
        await using var first = new DeltaHarness();
        await using var second = new DeltaHarness();
        var firstInitial = await Start(first);
        var secondInitial = await Start(second);
        var firstWatch = await Ack(first, firstInitial.Nonce);
        var secondWatch = await Ack(second, secondInitial.Nonce);
        var shared = snapshot.GetVersionMap(ResourceTypeUrls.Route)!;
        Assert.Same(shared, firstWatch.Subscription.ReturnedResources);
        Assert.Same(shared, secondWatch.Subscription.ReturnedResources);

        var holdSend = Signal();
        first.Sending = (_, _) => new ValueTask(Wait(holdSend.Task));
        try
        {
            first.Input.Push(DeltaStreamFixtures.Request(ResourceTypeUrls.Route, removals: ["route"]));
            _ = await first.NextWatch();
            var resend = await first.NextResponse();
            Assert.Equal("route", Assert.Single(resend.Resources).Name);
            Assert.Empty(resend.RemovedResources);
            Assert.Equal("", firstWatch.Subscription.ReturnedResources["route"]);
            Assert.Same(shared, secondWatch.Subscription.ReturnedResources);
            Assert.Equal(Assert.Single(secondInitial.Resources, resource => resource.Name == "route").Version, shared["route"]);
            first.Input.Push(DeltaStreamFixtures.Request(ResourceTypeUrls.Route, nonce: resend.Nonce));
        }
        finally
        {
            holdSend.SetResult();
        }

        Assert.Same(shared, (await first.NextWatch()).Subscription.ReturnedResources);
        Assert.Equal(2, cache.GetStatusInfo("node")!.NumDeltaWatches);

        var next = Routes("v2", Route("route", true));
        await cache.SetSnapshotAsync("node", next);
        foreach (var response in new[] { await first.NextResponse(), await second.NextResponse() })
        {
            Assert.True(Assert.Single(response.Resources).Resource_.Unpack<RouteConfiguration>().IgnorePortInHostMatching);
            Assert.Equal("other", Assert.Single(response.RemovedResources));
        }
        await first.Finish();
        await second.Finish();

        async Task<DeltaDiscoveryResponse> Start(DeltaHarness stream)
        {
            stream.Watcher.Create = cache.CreateDeltaWatch;
            stream.Start(ResourceTypeUrls.Route);
            var request = DeltaRequest();
            request.ResourceNamesSubscribe.Add("*");
            request.ResourceNamesSubscribe.Add("route");
            stream.Input.Push(request);
            _ = await stream.NextWatch();
            return await stream.NextResponse();
        }

        static Task<DeltaRegistration> Ack(DeltaHarness stream, string nonce)
        {
            stream.Input.Push(DeltaStreamFixtures.Request(ResourceTypeUrls.Route, nonce: nonce));
            return stream.NextWatch();
        }
    }

    [Fact]
    public async Task WildcardCustomComparer_PreservesOrdinalResourceNames()
    {
        var cache = new SnapshotCache();
        var route = Route("a");
        var snapshot = Routes("v1", route);
        snapshot.ConstructVersionMap();
        await cache.SetSnapshotAsync("node", snapshot);
        var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = snapshot.GetVersionMap(ResourceTypeUrls.Route)!["a"],
        };
        var subscription = SnapshotCacheFixtures.Subscription(wildcard: true, returned: known);
        using var watch = cache.CreateDeltaWatch(DeltaRequest("ack"), subscription);

        var response = await Wait(watch.Completion);
        var envelope = response.GetDeltaDiscoveryResponse();
        Assert.Equal("a", Assert.Single(envelope.Resources).Name);
        Assert.Equal("A", Assert.Single(envelope.RemovedResources));
        Assert.Equal("a", Assert.Single(response.ReturnedResources).Key);
    }

    private static Snapshot Routes(string version, params IMessage[] routes) =>
        new(version, new Dictionary<string, IEnumerable<IMessage?>> { [ResourceTypeUrls.Route] = routes });

    private static RouteConfiguration Route(string name, bool updated = false) =>
        new() { Name = name, IgnorePortInHostMatching = updated };

    private static DeltaDiscoveryRequest DeltaRequest(string nonce = "") =>
        new() { TypeUrl = ResourceTypeUrls.Route, ResponseNonce = nonce, Node = new Node { Id = "node" } };
}
