using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotCacheTests
{
    [Fact]
    public async Task MissingSnapshot_RegistersUntilPublicationAndConsumesWatchOnce()
    {
        var cache = new SnapshotCache();
        var request = Request(ResourceTypeUrls.Listener);
        using var watch = cache.CreateWatch(request, Subscription(wildcard: true));
        var status = Assert.IsAssignableFrom<IStatusInfo>(cache.GetStatusInfo("node"));
        Assert.False(watch.Completion.IsCompleted);
        Assert.Equal(1, status.NumWatches);
        Assert.Same(request.Node, status.Node);
        Assert.Equal(new[] { "node" }, cache.GetStatusKeys());
        var snapshot = ResourceFixtures.Snapshot("opaque/v2", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }]));
        using var cancellation = new CancellationTokenSource();
        var state = new object();
        var context = new ResponseContext(cancellation.Token, state);
        await cache.SetSnapshotAsync("node", snapshot, context);
        var response = await watch.Completion;
        Assert.Same(request, response.Request);
        Assert.Equal("opaque/v2", response.ResponseVersion);
        Assert.Equal(context, response.Context);
        Assert.Equal("opaque/v2", response.ReturnedResources["listener"]);
        Assert.Equal(0, status.NumWatches);
        Assert.Same(snapshot, cache.GetSnapshot("node"));
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("later"));
        Assert.Same(response, await watch.Completion);
        Assert.Equal("opaque/v2", response.GetDiscoveryResponse().VersionInfo);
    }

    [Fact]
    public async Task ImmediateAndSameVersionPending_UseReturnedResourceMetadata()
    {
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }])));
        Assert.Empty(cache.GetStatusKeys());
        using var immediate = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        Assert.True(immediate.Completion.IsCompletedSuccessfully);
        var first = await immediate.Completion;
        Assert.Equal(default, first.Context);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
        using var pending = cache.CreateWatch(Request(ResourceTypeUrls.Listener, "v1"),
            Subscription(wildcard: true, returned: first.ReturnedResources));
        Assert.False(pending.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }])));
        Assert.False(pending.Completion.IsCompleted);
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v2", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }])));
        Assert.Equal("v2", (await pending.Completion).ResponseVersion);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    [Fact]
    public async Task Cancellation_ReleasesRegistrationAndRetainsNodeStatus()
    {
        var cache = new SnapshotCache();
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var status = cache.GetStatusInfo("node")!;
        watch.Dispose();
        watch.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch.Completion);
        Assert.True(watch.Completion.IsCanceled);
        Assert.Equal(0, status.NumWatches);
        Assert.Equal(new[] { "node" }, cache.GetStatusKeys());
        Assert.Same(status, cache.GetStatusInfo("node"));
        Assert.Null(cache.GetStatusInfo("missing"));
    }

    [Fact]
    public async Task Clear_RemovesIndexedStateWithoutCompletingOldWatchOrCancelingNewOne()
    {
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v1"));
        using var old = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var previous = cache.GetStatusInfo("node")!;
        Assert.Equal(1, previous.NumWatches);
        cache.ClearSnapshot("node");
        cache.ClearSnapshot("node");
        Assert.Null(cache.GetStatusInfo("node"));
        Assert.Empty(cache.GetStatusKeys());
        Assert.Contains("node", Assert.Throws<KeyNotFoundException>(() => cache.GetSnapshot("node")).Message);
        Assert.False(old.Completion.IsCompleted);
        Assert.Equal(1, previous.NumWatches);
        using var current = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        old.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old.Completion);
        Assert.True(old.Completion.IsCanceled);
        Assert.False(current.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        Assert.Equal(1, previous.NumWatches);
        current.Dispose();
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    [Fact]
    public void CustomHashAndClock_DriveLiveStatusAndRetainFirstNode()
    {
        var firstTime = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        var clock = new TestTimeProvider(firstTime);
        var cache = new SnapshotCache(nodeHasher: new FixedNodeHasher("tenant"), timeProvider: clock);
        var first = Request(ResourceTypeUrls.Listener, nodeId: "first");
        using var one = cache.CreateWatch(first, Subscription(wildcard: true));
        var status = cache.GetStatusInfo("tenant")!;
        Assert.Same(first.Node, status.Node);
        Assert.Equal(firstTime, status.LastWatchRequestTime);
        Assert.Equal(default, status.LastDeltaWatchRequestTime);
        Assert.Equal(0, status.NumDeltaWatches);
        clock.Current = firstTime.AddMinutes(1);
        using var two = cache.CreateWatch(Request(ResourceTypeUrls.Listener, nodeId: "second"), Subscription(wildcard: true));
        Assert.Same(first.Node, status.Node);
        Assert.Equal(clock.Current, status.LastWatchRequestTime);
        Assert.Equal(2, clock.Reads);
        Assert.Equal(2, status.NumWatches);
        Assert.Null(cache.GetStatusInfo("first"));
        Assert.Equal(new[] { "tenant" }, cache.GetStatusKeys());
        one.Dispose();
        Assert.Equal(1, status.NumWatches);
    }

    [Fact]
    public void DefaultHasher_UsesNodeIdAndEmptyKeyForAbsentNode()
    {
        var hash = new IdNodeHasher();
        Assert.Equal("node-id", hash.Hash(new Node { Id = "node-id" }));
        Assert.Equal("", hash.Hash(null));
        Assert.Equal("", hash.Hash(new Node()));
        var cache = new SnapshotCache();
        using var watch = cache.CreateWatch(new DiscoveryRequest { TypeUrl = ResourceTypeUrls.Listener }, Subscription(wildcard: true));
        Assert.Equal(new[] { "" }, cache.GetStatusKeys());
        Assert.Null(cache.GetStatusInfo("")!.Node);
        Assert.Equal(1, cache.GetStatusInfo("")!.NumWatches);
    }

    [Fact]
    public async Task Publication_DoesNotValidateOrCheckConsistency()
    {
        var invalid = new Cluster { Type = Cluster.Types.DiscoveryType.Eds };
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Cluster, [invalid]));
        Assert.Throws<InvalidOperationException>(snapshot.EnsureConsistent);
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", snapshot);
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Cluster), Subscription(wildcard: true));
        var response = await watch.Completion;
        Assert.Same(snapshot, cache.GetSnapshot("node"));
        Assert.Equal("", Assert.Single(response.ReturnedResources).Key);
        Assert.Single(response.GetDiscoveryResponse().Resources);
        Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Cluster));
    }

    [Fact]
    public async Task CustomSnapshot_CanServeAnArbitraryTypeUrl()
    {
        const string typeUrl = "application/custom";
        var message = new Listener { Name = "custom" };
        var snapshot = new SingleResourceSnapshot(typeUrl, new ResourceEntry(message));
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", snapshot);
        using var watch = cache.CreateWatch(Request(typeUrl), Subscription(wildcard: true));
        var response = await watch.Completion;
        Assert.Same(snapshot, cache.GetSnapshot("node"));
        Assert.Equal("application/v1", response.ResponseVersion);
        Assert.Equal("application/v1", response.ReturnedResources["custom"]);
        var payload = Assert.Single(response.GetDiscoveryResponse().Resources);
        Assert.Equal(typeUrl, payload.TypeUrl);
        Assert.Equal(message, Listener.Parser.ParseFrom(payload.Value));
    }

    [Fact]
    public async Task CustomSnapshotFailure_IsSynchronousDuringCreationAndLeavesStatus()
    {
        var failure = new InvalidOperationException("snapshot read failed");
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", new ObservedSnapshot(ResourceFixtures.Snapshot("v1"), () => throw failure));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true))));
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
        Assert.Equal(new[] { "node" }, cache.GetStatusKeys());
    }

    [Fact]
    public async Task FailedPublication_StoresSnapshotAndKeepsWatchForRetry()
    {
        var cache = new SnapshotCache();
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var failure = new InvalidOperationException("snapshot read failed");
        var broken = new ObservedSnapshot(ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [])), () => throw failure);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => cache.SetSnapshotAsync("node", broken)));
        Assert.Same(broken, cache.GetSnapshot("node"));
        Assert.False(watch.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v2", (ResourceTypeUrls.Listener, [new Listener { Name = "retry" }])));
        Assert.Equal("v2", (await watch.Completion).ResponseVersion);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    [Fact]
    public async Task CanceledPublication_CanBeRetriedAfterSnapshotWasStored()
    {
        var cache = new SnapshotCache();
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Listener), Subscription(wildcard: true));
        var snapshot = ResourceFixtures.Snapshot("v1", (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => cache.SetSnapshotAsync("node", snapshot, new ResponseContext(cancellation.Token)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Same(snapshot, cache.GetSnapshot("node"));
        Assert.False(watch.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        await cache.SetSnapshotAsync("node", snapshot);
        Assert.Equal("v1", (await watch.Completion).ResponseVersion);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    [Fact]
    public void DeltaDelivery_IsExplicitlyUnavailableInTheSotwSlice()
    {
        var cache = new SnapshotCache();
        Assert.Contains("Delta", Assert.Throws<NotSupportedException>(() =>
            cache.CreateDeltaWatch(new DeltaDiscoveryRequest(), Subscription())).Message);
        Assert.Empty(cache.GetStatusKeys());
    }
}
