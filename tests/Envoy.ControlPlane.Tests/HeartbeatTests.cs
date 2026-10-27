using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.HeartbeatFixtures;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;
using DiscoveryResource = Envoy.Service.Discovery.V3.Resource;

namespace Envoy.ControlPlane.Tests;

public sealed class HeartbeatTests
{
    [Fact]
    public async Task SourceCases_MatchDeclaredOutcomesAndExportActualObservations()
    {
        var inputs = HeartbeatCorpus.Cases();
        Assert.Equal(13, inputs.Length);
        Assert.Equal(13, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var results = new List<HeartbeatResult>();
        foreach (var input in inputs)
        {
            var result = await HeartbeatCorpus.EvaluateAsync(input);
            Assert.Equal(input.ExpectedPending, result.Observed.Watches);
            Assert.Equal(input.ExpectedNames.Length != 0, result.Observed.Response is not null);
            if (result.Observed.Response is { } response)
            {
                Assert.Equal("v1", response.Version);
                Assert.Equal(ResourceTypeUrls.Route, response.TypeUrl);
                Assert.Equal(input.ExpectedNames, response.Resources.Select(resource => resource.Name));
                Assert.All(response.Resources, resource => Assert.Equal(input.Normal, resource.HasPayload));
            }
            results.Add(result);
        }
        HeartbeatCorpus.WriteResults(results);
    }

    [Fact]
    public async Task FakeTime_RefreshesRepeatedWatchesWithoutAdvancingVersionsOrExpiringSnapshots()
    {
        var clock = new FakeTimeProvider();
        var cache = new SnapshotCache(timeProvider: clock);
        var snapshot = Routes("v1", Entry("a", TimeSpan.FromSeconds(5)), Entry("plain"));
        await cache.SetSnapshotAsync("node", snapshot);
        var known = new Dictionary<string, string> { ["a"] = "v1", ["plain"] = "v1" };
        var subscription = Subscription(wildcard: true, returned: known);
        using var first = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), subscription);
        var requestTime = cache.GetStatusInfo("node")!.LastWatchRequestTime;
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(first.Completion.IsCompleted);
        using var stop = new CancellationTokenSource();
        var context = new ResponseContext(stop.Token, new object());
        Task loop = cache.RunHeartbeatsAsync(Interval, context);
        try
        {
            clock.Advance(Interval - TimeSpan.FromMilliseconds(1));
            Assert.False(first.Completion.IsCompleted);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            var response = await Wait(first.Completion);
            var envelope = response.GetDiscoveryResponse();
            var heartbeat = Assert.Single(envelope.Resources).Unpack<DiscoveryResource>();
            Assert.Equal("a", heartbeat.Name);
            Assert.Equal(TimeSpan.FromSeconds(5), heartbeat.Ttl.ToTimeSpan());
            Assert.Null(heartbeat.Resource_);
            Assert.Empty(heartbeat.Version);
            Assert.Equal("v1", envelope.VersionInfo);
            Assert.Same(known, response.ReturnedResources);
            Assert.Equal(context, response.Context);
            Assert.Same(envelope, response.GetDiscoveryResponse());
            Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
            Assert.Equal(requestTime, cache.GetStatusInfo("node")!.LastWatchRequestTime);
            using var second = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), subscription);
            clock.Advance(Interval);
            Assert.Equal("v1", (await Wait(second.Completion)).ResponseVersion);
        }
        finally
        {
            stop.Cancel();
            await Wait(loop);
        }
        using var afterStop = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), subscription);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(afterStop.Completion.IsCompleted);
        Assert.Same(snapshot, cache.GetSnapshot("node"));
        Assert.Equal(2, cache.GetSnapshot("node").GetResources(ResourceTypeUrls.Route)!.Count);
        await cache.SetSnapshotAsync("node", Routes("v2", Entry("a", TimeSpan.FromSeconds(5)), Entry("plain")));
        Assert.Equal("v2", (await Wait(afterStop.Completion)).ResponseVersion);
    }

    [Fact]
    public async Task PrefixAndNonTtlWatches_RemainPendingAfterAnObservedScan()
    {
        var clock = new FakeTimeProvider();
        var cache = new SnapshotCache(timeProvider: clock);
        await cache.SetSnapshotAsync("node", Routes("v1", Entry("sentinel", TimeSpan.Zero), Entry("collection/a", TimeSpan.FromSeconds(5)), Entry("plain")));
        using var sentinel = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), Known("sentinel"));
        using var prefix = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), Subscription(
            returned: new Dictionary<string, string> { ["collection/a"] = "v1" }, prefixes: ["collection/"]));
        using var plain = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), Known("plain"));
        using var missing = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1", nodeId: "missing"), Known("a"));
        using var stop = new CancellationTokenSource();
        Task loop = cache.RunHeartbeatsAsync(Interval, new ResponseContext(stop.Token));
        try
        {
            clock.Advance(Interval);
            var wrapper = Assert.Single((await Wait(sentinel.Completion)).GetDiscoveryResponse().Resources).Unpack<DiscoveryResource>();
            Assert.Equal(TimeSpan.Zero, wrapper.Ttl.ToTimeSpan());
        }
        finally
        {
            stop.Cancel();
            await Wait(loop);
        }
        Assert.False(prefix.Completion.IsCompleted);
        Assert.False(plain.Completion.IsCompleted);
        Assert.False(missing.Completion.IsCompleted);
        Assert.Equal(2, cache.GetStatusInfo("node")!.NumWatches);
        Assert.Equal(1, cache.GetStatusInfo("missing")!.NumWatches);
    }

    [Fact]
    public async Task BlockedHeartbeat_StopRetainsWatchAndAllowsNormalPublication()
    {
        var clock = new TrackingTimeProvider();
        var cache = new SnapshotCache(timeProvider: clock);
        await cache.SetSnapshotAsync("node", Routes("v1", Entry("a", TimeSpan.FromSeconds(5))));
        var channel = Channel.CreateBounded<ISotwResponse>(1);
        var marker = new TestSotwResponse(Request(ResourceTypeUrls.Route), "marker", new Dictionary<string, string>(), default, () => new DiscoveryResponse());
        Assert.True(channel.Writer.TryWrite(marker));
        var observed = new HeartbeatWriter(channel.Writer);
        await using var registration = (IAsyncDisposable)cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), Known("a"), observed);
        using var stop = new CancellationTokenSource();
        Task loop = cache.RunHeartbeatsAsync(Interval, new ResponseContext(stop.Token));
        Assert.Equal(1, clock.ActiveTimers);
        clock.Advance(Interval);
        await Wait(observed.Started.Task);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        stop.Cancel();
        await Wait(loop);
        Assert.Equal(0, clock.ActiveTimers);
        Assert.Same(marker, await channel.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumWatches);
        await cache.SetSnapshotAsync("node", Routes("v2", Entry("a", TimeSpan.FromSeconds(5))));
        var response = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("v2", response.ResponseVersion);
        Assert.NotNull(Assert.Single(response.GetDiscoveryResponse().Resources).Unpack<DiscoveryResource>().Resource_);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
    }

    [Fact]
    public async Task WatchDisposal_SettlesBlockedHeartbeatWithoutClosingTheWriter()
    {
        var clock = new FakeTimeProvider();
        var cache = new SnapshotCache(timeProvider: clock);
        await cache.SetSnapshotAsync("node", Routes("v1", Entry("a", TimeSpan.FromSeconds(5))));
        var channel = Channel.CreateBounded<ISotwResponse>(1);
        var marker = new TestSotwResponse(Request(ResourceTypeUrls.Route), "marker", new Dictionary<string, string>(), default, () => new DiscoveryResponse());
        Assert.True(channel.Writer.TryWrite(marker));
        var observed = new HeartbeatWriter(channel.Writer);
        var registration = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), Known("a"), observed);
        using var stop = new CancellationTokenSource();
        Task loop = cache.RunHeartbeatsAsync(Interval, new ResponseContext(stop.Token));
        try
        {
            clock.Advance(Interval);
            await Wait(observed.Started.Task);
            await ((IAsyncDisposable)registration).DisposeAsync();
            Assert.Equal(0, cache.GetStatusInfo("node")!.NumWatches);
        }
        finally
        {
            stop.Cancel();
            await Wait(loop);
        }
        Assert.Same(marker, await channel.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(channel.Reader.TryRead(out _));
        Assert.True(channel.Writer.TryWrite(marker));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidInterval_IsRejected(int milliseconds)
    {
        var cache = new SnapshotCache(timeProvider: new FakeTimeProvider());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cache.RunHeartbeatsAsync(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public async Task HeartbeatPayload_IsNotSerialized_ButNormalTtlResponseIsLazy()
    {
        var clock = new FakeTimeProvider();
        var failure = new InvalidOperationException("payload cannot be serialized");
        var message = new ThrowingResource(failure);
        var cache = new SnapshotCache(timeProvider: clock);
        await cache.SetSnapshotAsync("node", new SingleResourceSnapshot("application/custom", new ResourceEntry(message, TimeSpan.FromSeconds(5))));
        using var watch = cache.CreateWatch(Request("application/custom", "application/v1"), Subscription(["broken"], returned: new Dictionary<string, string> { ["broken"] = "application/v1" }));
        using var stop = new CancellationTokenSource();
        Task loop = cache.RunHeartbeatsAsync(Interval, new ResponseContext(stop.Token));
        try
        {
            clock.Advance(Interval);
            var response = await Wait(watch.Completion);
            var wrapper = Assert.Single(response.GetDiscoveryResponse().Resources).Unpack<DiscoveryResource>();
            Assert.Equal("broken", wrapper.Name);
            Assert.Null(wrapper.Resource_);
            Assert.Equal(0, message.Attempts);
        }
        finally
        {
            stop.Cancel();
            await Wait(loop);
        }
        using var normal = cache.CreateWatch(Request("application/custom"), Subscription(["broken"]));
        var raw = await Wait(normal.Completion);
        Assert.Equal(0, message.Attempts);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(raw.GetDiscoveryResponse));
        Assert.Equal(1, message.Attempts);
    }

    [Fact]
    public async Task TimerShutdown_DoesNotCancelDeltaWatches_AndPassThroughRetainsCallerTtl()
    {
        var clock = new FakeTimeProvider();
        var cache = new SnapshotCache(timeProvider: clock);
        var snapshot = Routes("v1", Entry("a", TimeSpan.FromSeconds(5)));
        snapshot.ConstructVersionMap();
        await cache.SetSnapshotAsync("node", snapshot);
        var request = new DeltaDiscoveryRequest { TypeUrl = ResourceTypeUrls.Route, Node = new Node { Id = "node" }, ResponseNonce = "accepted" };
        using var delta = cache.CreateDeltaWatch(request, Subscription(["a"], returned: snapshot.GetVersionMap(ResourceTypeUrls.Route)));
        using var sentinel = cache.CreateWatch(Request(ResourceTypeUrls.Route, "v1"), Known("a"));
        using var stop = new CancellationTokenSource();
        Task loop = cache.RunHeartbeatsAsync(Interval, new ResponseContext(stop.Token));
        try
        {
            clock.Advance(Interval);
            await Wait(sentinel.Completion);
        }
        finally
        {
            stop.Cancel();
            await Wait(loop);
        }
        Assert.False(delta.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumDeltaWatches);
        var envelope = new DeltaDiscoveryResponse { TypeUrl = ResourceTypeUrls.Route };
        envelope.Resources.Add(new DiscoveryResource { Name = "a", Version = "same", Ttl = Duration.FromTimeSpan(TimeSpan.FromSeconds(5)) });
        var passthrough = new PassthroughDeltaResponse(request, envelope, new Dictionary<string, string> { ["a"] = "same" });
        Assert.Same(envelope, passthrough.GetDeltaDiscoveryResponse());
        Assert.Null(Assert.Single(passthrough.GetDeltaDiscoveryResponse().Resources).Resource_);
        Assert.Equal(TimeSpan.FromSeconds(5), envelope.Resources[0].Ttl.ToTimeSpan());
    }
}
