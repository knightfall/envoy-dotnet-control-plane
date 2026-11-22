using System.Threading.Channels;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.HeartbeatFixtures;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class WatchWriterReentrancyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Writer_CanReadTheCacheFromAnotherThread(bool linear, bool delta, bool immediate)
    {
        var snapshotCache = new SnapshotCache();
        var linearCache = new LinearCache(ResourceTypeUrls.Route);
        IConfigWatcher cache = linear ? linearCache : snapshotCache;
        Task PublishAsync() => linear
            ? linearCache.UpdateResourceAsync("a", new RouteConfiguration { Name = "a" })
            : snapshotCache.SetSnapshotAsync("node", Routes("1", Entry("a")));
        if (immediate)
        {
            await PublishAsync();
        }

        void ReadCache() => ReadOnAnotherThread(() =>
        {
            if (linear)
            {
                Assert.Equal(1, linearCache.NumResources);
            }
            else
            {
                Assert.Equal("1", snapshotCache.GetSnapshot("node").GetVersion(ResourceTypeUrls.Route));
            }
        });

        if (delta)
        {
            var writer = new CallbackWriter<IDeltaResponse>(ReadCache);
            using var registration = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]), writer);
            if (!immediate)
            {
                Assert.False(writer.Response.IsCompleted);
                await PublishAsync();
            }
            Assert.Equal("1", (await Wait(writer.Response)).ResponseVersion);
        }
        else
        {
            var writer = new CallbackWriter<ISotwResponse>(ReadCache);
            using var registration = cache.CreateWatch(Request(ResourceTypeUrls.Route, immediate ? "" : "0"),
                new SotwSubscription(["a"]), writer);
            if (!immediate)
            {
                Assert.False(writer.Response.IsCompleted);
                await PublishAsync();
            }
            Assert.Equal("1", (await Wait(writer.Response)).ResponseVersion);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearPublication_WriterCanDisposeAnotherSelectedWatch(bool delta)
    {
        var cache = new LinearCache(ResourceTypeUrls.Route);
        if (delta)
        {
            await CheckPublicationAsync<IDeltaResponse>(
                writer => cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]), writer));
        }
        else
        {
            await CheckPublicationAsync<ISotwResponse>(
                writer => cache.CreateWatch(Request(ResourceTypeUrls.Route, "0"), new SotwSubscription(["a"]), writer));
        }

        async Task CheckPublicationAsync<TResponse>(Func<ChannelWriter<TResponse>, IDisposable> watch) where TResponse : IWatchResponse
        {
            IDisposable? second = null;
            var firstWriter = new CallbackWriter<TResponse>(() => second!.Dispose());
            using var first = watch(firstWriter);
            var secondWriter = new CallbackWriter<TResponse>(first.Dispose);
            using (second = watch(secondWriter))
            {
                Assert.Equal(2, cache.NumCacheWatches);
                await cache.UpdateResourceAsync("a", new RouteConfiguration { Name = "a" }, TestContext.Current.CancellationToken);
                Assert.NotEqual(firstWriter.Response.IsCompletedSuccessfully, secondWriter.Response.IsCompletedSuccessfully);
                Assert.Equal(0, cache.NumCacheWatches);
            }
        }
    }

    [Fact]
    public async Task HeartbeatWriter_CanRegisterAnotherNodeDuringDelivery()
    {
        var clock = new FakeTimeProvider();
        var cache = new SnapshotCache(timeProvider: clock);
        await cache.SetSnapshotAsync("node", Routes("v1", Entry("a", TimeSpan.FromSeconds(5))));
        IDisposable? inserted = null;
        var writer = new CallbackWriter<ISotwResponse>(() =>
            inserted = cache.CreateWatch(Request(ResourceTypeUrls.Route, nodeId: "new-node"), new SotwSubscription(["a"])));
        using var heartbeat = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"),
            Known("a"), writer);
        using var stop = new CancellationTokenSource();
        var loop = cache.RunHeartbeatsAsync(Interval, new ResponseContext(stop.Token));
        try
        {
            clock.Advance(Interval);
            Assert.Equal("v1", (await Wait(writer.Response)).ResponseVersion);
            Assert.Equal(1, cache.GetStatusInfo("new-node")!.NumWatches);
        }
        finally
        {
            stop.Cancel();
            inserted?.Dispose();
            await Wait(loop);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedImmediateWriter_CanBeDisposedSynchronouslyAndCleanupIsIdempotent(bool linear)
    {
        var snapshotCache = new SnapshotCache();
        var linearCache = new LinearCache(ResourceTypeUrls.Route, new Dictionary<string, IMessage?>
        {
            ["a"] = new RouteConfiguration { Name = "a" },
        });
        IConfigWatcher cache = linear ? linearCache : snapshotCache;
        await snapshotCache.SetSnapshotAsync("node", Routes("v1", Entry("a")));
        var closed = Channel.CreateUnbounded<IDeltaResponse>();
        closed.Writer.Complete();
        var registration = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]), closed.Writer);

        registration.Dispose();
        registration.Dispose();
        var cleanup = ((IAsyncDisposable)registration).DisposeAsync().AsTask();
        await Wait(cleanup);
        Assert.Same(cleanup, ((IAsyncDisposable)registration).DisposeAsync().AsTask());
        Assert.False(closed.Reader.TryRead(out _));

        using var fresh = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]));
        Assert.Equal("a", Assert.Single((await Wait(fresh.Completion)).GetDeltaDiscoveryResponse().Resources).Name);
    }

    private static DeltaDiscoveryRequest DeltaRequest() => new()
    {
        Node = new Envoy.Config.Core.V3.Node { Id = "node" },
        TypeUrl = ResourceTypeUrls.Route,
    };

    private static void ReadOnAnotherThread(Action read) =>
        Task.Run(read).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();

    private sealed class CallbackWriter<TResponse>(Action callback) : ChannelWriter<TResponse>
    {
        private readonly TaskCompletionSource<TResponse> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<TResponse> Response => response.Task;

        public override bool TryWrite(TResponse item)
        {
            try
            {
                callback();
                response.SetResult(item);
                return true;
            }
            catch (Exception error)
            {
                response.SetException(error);
                throw;
            }
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
