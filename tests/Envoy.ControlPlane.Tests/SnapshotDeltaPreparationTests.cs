using System.Security.Cryptography;
using System.Threading.Channels;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotDeltaPreparationTests
{
    [Fact]
    public async Task SharedSnapshot_PublishesCompleteHashesOnceAcrossIndependentCaches()
    {
        using var release = new SemaphoreSlim(0);
        var entered = Signal();
        var secondStarted = Signal();
        var directStarted = Signal();
        var route = new RouteConfiguration { Name = "route" };
        var observed = new ObservedResource(route, () =>
        {
            entered.TrySetResult();
            Assert.True(release.Wait(StreamFixtures.Timeout));
        });
        var cluster = new Cluster { Name = "cluster" };
        var snapshot = CreateSnapshot("v1", observed, cluster);
        var first = new SnapshotCache();
        var second = new SnapshotCache();
        await first.SetSnapshotAsync("node", snapshot);
        await second.SetSnapshotAsync("node", snapshot);

        var firstResponse = Task.Run(() => ReadInitialRoute(first), TestContext.Current.CancellationToken);
        Task<IDeltaResponse> secondResponse;
        Task direct;
        try
        {
            await Wait(entered.Task);
            secondResponse = Task.Run(() =>
            {
                secondStarted.SetResult();
                return ReadInitialRoute(second);
            }, TestContext.Current.CancellationToken);
            direct = Task.Run(() =>
            {
                directStarted.SetResult();
                snapshot.ConstructVersionMap();
            }, TestContext.Current.CancellationToken);
            await Wait(Task.WhenAll(secondStarted.Task, directStarted.Task));
            Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Cluster));
            Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Route));
            Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Endpoint));
        }
        finally
        {
            release.Release(3);
        }

        await Wait(direct);
        var responses = await Wait(Task.WhenAll(firstResponse, secondResponse));
        var versions = snapshot.GetVersionMap(ResourceTypeUrls.Route);
        Assert.NotNull(versions);
        Assert.Equal(Hash(route), versions["route"]);
        Assert.Equal(Hash(cluster), snapshot.GetVersionMap(ResourceTypeUrls.Cluster)!["cluster"]);
        Assert.Empty(snapshot.GetVersionMap(ResourceTypeUrls.Endpoint)!);
        Assert.Null(snapshot.GetVersionMap("unknown"));
        Assert.All(responses, response =>
        {
            Assert.Same(versions, response.ReturnedResources);
            Assert.Equal(route, Assert.Single(response.GetDeltaDiscoveryResponse().Resources).Resource_.Unpack<RouteConfiguration>());
        });
        Assert.Equal(1, observed.Attempts);
    }

    [Fact]
    public void FailedPreparation_LeavesNoPartialStateAndRetries()
    {
        var failure = new InvalidOperationException("first serialization fails");
        var route = new RouteConfiguration { Name = "route" };
        int attempt = 0;
        var observed = new ObservedResource(route, () =>
        {
            if (++attempt == 1)
            {
                throw failure;
            }
        });
        var cluster = new Cluster { Name = "cluster" };
        var snapshot = CreateSnapshot("v1", observed, cluster);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(snapshot.ConstructVersionMap));
        Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Cluster));
        Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Route));
        snapshot.ConstructVersionMap();

        Assert.Equal(Hash(cluster), snapshot.GetVersionMap(ResourceTypeUrls.Cluster)!["cluster"]);
        var versions = snapshot.GetVersionMap(ResourceTypeUrls.Route);
        Assert.NotNull(versions);
        Assert.Equal(Hash(route), versions["route"]);
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IDictionary<string, string>>(versions).Add("extra", ""));
        snapshot.ConstructVersionMap();
        Assert.Same(versions, snapshot.GetVersionMap(ResourceTypeUrls.Route));
        Assert.Equal(2, observed.Attempts);
    }

    [Fact]
    public async Task Publication_PreparesOutsideCacheGateAndSelectsNewlyRegisteredWatches()
    {
        using var release = new SemaphoreSlim(0);
        var entered = Signal();
        var initial = CreateSnapshot("v1", new RouteConfiguration { Name = "route" });
        var updated = new RouteConfiguration { Name = "route", IgnorePortInHostMatching = true };
        var candidate = CreateSnapshot("v2", new ObservedResource(updated, () =>
        {
            entered.SetResult();
            Assert.True(release.Wait(StreamFixtures.Timeout));
        }));
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", initial);
        initial.ConstructVersionMap();
        var known = initial.GetVersionMap(ResourceTypeUrls.Route)!;
        using var first = cache.CreateDeltaWatch(DeltaRequest("ack"), new DeltaSubscription(initialResourceVersions: known));

        var publication = Task.Run(() => cache.SetSnapshotAsync("node", candidate), TestContext.Current.CancellationToken);
        IWatch<IDeltaResponse> second;
        try
        {
            await Wait(entered.Task);
            await Wait(cache.SetSnapshotAsync("other", initial));
            Assert.Same(initial, cache.GetSnapshot("other"));
            second = await Wait(Task.Run(() =>
            {
                Assert.Same(initial, cache.GetSnapshot("node"));
                return cache.CreateDeltaWatch(DeltaRequest("ack"), new DeltaSubscription(initialResourceVersions: known));
            }, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Release();
        }

        await Wait(publication);
        Assert.All(await Wait(Task.WhenAll(first.Completion, ReadAndDispose(second))), response =>
        {
            Assert.Equal("v2", response.ResponseVersion);
            Assert.Equal(updated, Assert.Single(response.GetDeltaDiscoveryResponse().Resources).Resource_.Unpack<RouteConfiguration>());
        });
    }

    [Fact]
    public async Task Watch_RechecksSnapshotIfPublicationReplacesItDuringPreparation()
    {
        using var release = new SemaphoreSlim(0);
        var entered = Signal();
        var oldSnapshot = CreateSnapshot("v1", new ObservedResource(new RouteConfiguration { Name = "old" }, () =>
        {
            entered.SetResult();
            Assert.True(release.Wait(StreamFixtures.Timeout));
        }));
        var current = CreateSnapshot("v2", new RouteConfiguration { Name = "current" });
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", oldSnapshot);

        var response = Task.Run(() => ReadInitialRoute(cache), TestContext.Current.CancellationToken);
        try
        {
            await Wait(entered.Task);
            await Wait(Task.Run(() => cache.SetSnapshotAsync("node", current), TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Release();
        }

        var result = await Wait(response);
        Assert.Equal("v2", result.ResponseVersion);
        Assert.Equal("current", Assert.Single(result.GetDeltaDiscoveryResponse().Resources).Name);
    }

    [Fact]
    public async Task WatchClosesDuringFirstPublication_StatusStaysAbsentUntilAnotherWatch()
    {
        using var release = new SemaphoreSlim(0);
        var entered = Signal();
        var candidate = CreateSnapshot("v1", new ObservedResource(new RouteConfiguration { Name = "route" }, () =>
        {
            entered.TrySetResult();
            Assert.True(release.Wait(StreamFixtures.Timeout));
        }));
        var cache = new SnapshotCache();
        using var delta = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription());
        var previousStatus = cache.GetStatusInfo("node");
        var publication = Task.Run(() => cache.SetSnapshotAsync("node", candidate), TestContext.Current.CancellationToken);
        try
        {
            await Wait(entered.Task);
            delta.Dispose();
            Assert.Null(cache.GetStatusInfo("node"));
        }
        finally
        {
            release.Release();
        }

        await Wait(publication);
        Assert.Same(candidate, cache.GetSnapshot("node"));
        Assert.Null(cache.GetStatusInfo("node"));
        using var next = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription());
        Assert.Equal("v1", (await Wait(next.Completion)).ResponseVersion);
        Assert.NotSame(previousStatus, cache.GetStatusInfo("node"));
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumDeltaWatches);
    }

    [Fact]
    public async Task Publication_EnqueuesDeltaResponsesInAdsTypeOrder()
    {
        var cache = new SnapshotCache(ads: true);
        var responses = Channel.CreateUnbounded<IDeltaResponse>();
        using var route = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(), responses.Writer);
        var clusterRequest = DeltaRequest();
        clusterRequest.TypeUrl = ResourceTypeUrls.Cluster;
        using var cluster = cache.CreateDeltaWatch(clusterRequest, new DeltaSubscription(), responses.Writer);

        await cache.SetSnapshotAsync("node", CreateSnapshot("v1", new RouteConfiguration { Name = "route" },
            new Cluster { Name = "cluster" }));

        Assert.True(responses.Reader.TryRead(out var first));
        Assert.True(responses.Reader.TryRead(out var second));
        Assert.Same(clusterRequest, first.Request);
        Assert.Equal("cluster", Assert.Single(first.GetDeltaDiscoveryResponse().Resources).Name);
        Assert.Equal(ResourceTypeUrls.Route, second.Request.TypeUrl);
        Assert.Equal("route", Assert.Single(second.GetDeltaDiscoveryResponse().Resources).Name);
        Assert.False(responses.Reader.TryRead(out _));
    }

    [Fact]
    public async Task FailedPublication_InstallsSnapshotAndDeliversSotwBeforeRetryingDelta()
    {
        var failure = new InvalidOperationException("first serialization fails");
        var route = new RouteConfiguration { Name = "route" };
        int attempt = 0;
        var snapshot = CreateSnapshot("v1", new ObservedResource(route, () =>
        {
            if (++attempt == 1)
            {
                throw failure;
            }
        }));
        var cache = new SnapshotCache();
        using var sotw = cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Route), Subscription(wildcard: true));
        using var delta = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription());

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SetSnapshotAsync("node", snapshot)));
        Assert.Same(snapshot, cache.GetSnapshot("node"));
        Assert.Equal("v1", (await Wait(sotw.Completion)).ResponseVersion);
        Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Route));
        Assert.False(delta.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumDeltaWatches);

        await cache.SetSnapshotAsync("node", snapshot);
        var result = await Wait(delta.Completion);
        Assert.Equal(Hash(route), Assert.Single(result.GetDeltaDiscoveryResponse().Resources).Version);
        Assert.Equal(2, attempt);
    }

    private static Snapshot CreateSnapshot(string version, IMessage route, Cluster? cluster = null)
    {
        var groups = new Dictionary<string, IEnumerable<IMessage?>> { [ResourceTypeUrls.Route] = [route] };
        if (cluster is not null)
        {
            groups.Add(ResourceTypeUrls.Cluster, [cluster]);
        }
        return new Snapshot(version, groups);
    }

    private static Task<IDeltaResponse> ReadInitialRoute(SnapshotCache cache) =>
        ReadAndDispose(cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription()));

    private static async Task<IDeltaResponse> ReadAndDispose(IWatch<IDeltaResponse> watch)
    {
        using (watch)
        {
            return await Wait(watch.Completion);
        }
    }

    private static DeltaDiscoveryRequest DeltaRequest(string nonce = "") =>
        new() { TypeUrl = ResourceTypeUrls.Route, ResponseNonce = nonce, Node = new Node { Id = "node" } };

    private static string Hash(IMessage message) => Convert.ToHexStringLower(SHA256.HashData(message.ToByteArray()));
}
