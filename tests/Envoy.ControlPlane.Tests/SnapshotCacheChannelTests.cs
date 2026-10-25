using System.Threading.Channels;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotCacheChannelTests
{
    [Fact]
    public async Task FullDestination_AwaitsSpaceOutsideCacheLockAndSerializesPublications()
    {
        var cache = new SnapshotCache();
        var responses = Channel.CreateBounded<ISotwResponse>(1);
        var marker = Response(StreamFixtures.Request());
        Assert.True(responses.Writer.TryWrite(marker));
        using var watch = cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Listener), Subscription(wildcard: true), responses.Writer);
        var first = cache.SetSnapshotAsync("node", Listeners("v1"));
        Assert.False(first.IsCompleted);
        Assert.Equal("v1", cache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Listener));
        var second = cache.SetSnapshotAsync("node", Listeners("v2"));
        Assert.False(second.IsCompleted);
        Assert.Equal("v1", cache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Listener));
        // A different thread can still acquire cache synchronization while publication awaits capacity.
        using var other = await Wait(Task.Run(() => cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Listener, nodeId: "other"),
            Subscription(wildcard: true)), TestContext.Current.CancellationToken));
        Assert.Equal(1, cache.GetStatusInfo("other")!.NumWatches);
        Assert.Same(marker, await responses.Reader.ReadAsync(TestContext.Current.CancellationToken));
        await Wait(Task.WhenAll(first, second));
        Assert.Equal("v1", (await responses.Reader.ReadAsync(TestContext.Current.CancellationToken)).ResponseVersion);
        Assert.Equal("v2", cache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Listener));
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
        Assert.False(responses.Reader.TryRead(out _));
    }

    [Fact]
    public async Task DisposeAsync_SettlesBlockedDeliveryAndLeavesSharedWriterOpen()
    {
        var cache = new SnapshotCache();
        var responses = Channel.CreateBounded<ISotwResponse>(1);
        var marker = Response(StreamFixtures.Request());
        Assert.True(responses.Writer.TryWrite(marker));
        using var watch = cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Listener), Subscription(wildcard: true), responses.Writer);
        var publication = cache.SetSnapshotAsync("node", Listeners("v1"));
        Assert.False(publication.IsCompleted);
        await Wait(Assert.IsAssignableFrom<IAsyncDisposable>(watch).DisposeAsync().AsTask());
        await Wait(publication);
        Assert.Same(marker, await responses.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(responses.Reader.TryRead(out _));
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
        using var next = cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Listener, "v1"),
            Subscription(wildcard: true, returned: new Dictionary<string, string> { ["listener"] = "v1" }), responses.Writer);
        await cache.SetSnapshotAsync("node", Listeners("v2"));
        Assert.Equal("v2", (await responses.Reader.ReadAsync(TestContext.Current.CancellationToken)).ResponseVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledPublication_RetainsRegistrationForRetryOrDisposal(bool retry)
    {
        var cache = new SnapshotCache();
        var responses = Channel.CreateBounded<ISotwResponse>(1);
        Assert.True(responses.Writer.TryWrite(Response(StreamFixtures.Request())));
        using var watch = cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Listener), Subscription(wildcard: true), responses.Writer);
        using var cancellation = new CancellationTokenSource();
        var publication = cache.SetSnapshotAsync("node", Listeners("v1"), new ResponseContext(cancellation.Token));
        Assert.False(publication.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Wait(publication));
        Assert.Equal("v1", cache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Listener));
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        if (retry)
        {
            await responses.Reader.ReadAsync(TestContext.Current.CancellationToken);
            await cache.SetSnapshotAsync("node", Listeners("v2"));
            Assert.Equal("v2", (await responses.Reader.ReadAsync(TestContext.Current.CancellationToken)).ResponseVersion);
        }
        await Wait(Assert.IsAssignableFrom<IAsyncDisposable>(watch).DisposeAsync().AsTask());
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdsPublication_UsesResourcePriorityIndependentlyOfRegistrationOrder(bool ads)
    {
        var cache = new SnapshotCache(ads);
        var responses = Channel.CreateBounded<ISotwResponse>(2);
        using var listener = cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Listener), Subscription(wildcard: true), responses.Writer);
        using var cluster = cache.CreateWatch(SnapshotCacheFixtures.Request(ResourceTypeUrls.Cluster), Subscription(wildcard: true), responses.Writer);
        await cache.SetSnapshotAsync("node", ResourceFixtures.Snapshot("v1",
            (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }]),
            (ResourceTypeUrls.Cluster, [new Cluster { Name = "cluster" }])));
        var first = await responses.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var second = await responses.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ads ? ResourceTypeUrls.Cluster : ResourceTypeUrls.Listener, first.Request.TypeUrl);
        Assert.Equal(ads ? ResourceTypeUrls.Listener : ResourceTypeUrls.Cluster, second.Request.TypeUrl);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    private static Snapshot Listeners(string version) => ResourceFixtures.Snapshot(version,
        (ResourceTypeUrls.Listener, [new Listener { Name = "listener" }]));
}
