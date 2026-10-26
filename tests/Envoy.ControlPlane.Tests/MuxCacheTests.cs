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

namespace Envoy.ControlPlane.Tests;

public sealed class MuxCacheTests
{
    [Fact]
    public async Task SourceCases_MatchDeclaredOutcomesAndExportActualObservations()
    {
        var inputs = MuxCacheCorpus.Cases();
        Assert.Equal(12, inputs.Length);
        Assert.Equal(12, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var results = new List<MuxCacheResult>();
        foreach (var input in inputs)
        {
            var result = await MuxCacheCorpus.EvaluateAsync(input);
            Assert.Equal(input.ExpectedError, result.Observed.Error);
            Assert.Equal(input.ExpectedReady, result.Observed.Response is not null);
            Assert.Equal(input.Mode == "delta" ? 0 : 1, result.Observed.SotwClassifications);
            Assert.Equal(input.Mode == "delta" ? 1 : 0, result.Observed.DeltaClassifications);
            Assert.Equal(0, result.Observed.AfterSnapshot);
            Assert.Equal(0, result.Observed.AfterLinear);
            if (result.Observed.Response is { } response)
            {
                Assert.Equal(input.ExpectedVersion, response.Version);
                Assert.Equal(ResourceTypeUrls.Route, response.TypeUrl);
                Assert.Empty(response.Removed);
                Assert.Equal(input.Mode == "fetch" ? 0 : 1, response.Resources.Length);
                Assert.Equal(input.Mode == "fetch" ? 0 : 1, response.Returned.Count);
            }
            results.Add(result);
        }
        MuxCacheCorpus.WriteResults(results);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Classifiers_DelegateOriginalInputsAndRegistration(bool delta)
    {
        var sotwChild = new MuxProbeCache();
        var deltaChild = new MuxProbeCache();
        object? classified = null;
        var mux = new MuxCache(new Dictionary<string, IConfigWatcher> { ["sotw"] = sotwChild, ["delta"] = deltaChild },
            request =>
            {
                classified = request;
                return "sotw";
            }, request =>
            {
                classified = request;
                return "delta";
            });
        var subscription = Subscription(["a"], prefixes: ["collection/"]);
        var request = Request(ResourceTypeUrls.Route);
        var deltaRequest = new DeltaDiscoveryRequest { TypeUrl = ResourceTypeUrls.Route };
        var responses = Channel.CreateBounded<ISotwResponse>(1);
        var deltaResponses = Channel.CreateBounded<IDeltaResponse>(1);
        var registration = delta ? mux.CreateDeltaWatch(deltaRequest, subscription, deltaResponses.Writer)
            : mux.CreateWatch(request, subscription, responses.Writer);
        var selected = delta ? deltaChild : sotwChild;
        var other = delta ? sotwChild : deltaChild;
        Assert.Same(delta ? deltaRequest : (object)request, classified);
        Assert.Same(classified, selected.LastRequest);
        Assert.Same(subscription, selected.LastSubscription);
        Assert.Same(delta ? deltaResponses.Writer : (object)responses.Writer, selected.LastWriter);
        Assert.Same(selected.Registration, registration);
        Assert.Equal(1, selected.Calls);
        Assert.Equal(0, other.Calls);
        await ((IAsyncDisposable)registration).DisposeAsync();
        Assert.True(selected.Registration.AsynchronouslyDisposed);
        Assert.True(selected.Registration.Disposed);
        Assert.False(other.Registration.Disposed);
    }

    [Fact]
    public async Task Fetch_DelegatesOriginalContextTaskAndResponse()
    {
        var child = new MuxProbeCache();
        var request = Request(ResourceTypeUrls.Route);
        object? classified = null;
        IConfigFetcher mux = new MuxCache(new Dictionary<string, IConfigWatcher> { ["fetch"] = child },
            input =>
            {
                classified = input;
                return "fetch";
            }, _ => throw new InvalidOperationException("Delta classifier called by Fetch"));
        using var cancellation = new CancellationTokenSource();
        var context = new ResponseContext(cancellation.Token, new object());
        var response = new TestSotwResponse(request, "custom", new Dictionary<string, string>(), context, () => new DiscoveryResponse());
        Task<ISotwResponse> pending = mux.FetchAsync(request, context);
        Assert.Same(request, classified);
        Assert.Same(request, child.LastRequest);
        Assert.Equal(context.CancellationToken, child.LastContext.CancellationToken);
        Assert.Same(context.State, child.LastContext.State);
        Assert.Same(child.FetchCompletion.Task, pending);
        Assert.False(pending.IsCompleted);
        child.FetchCompletion.SetResult(response);
        Assert.Same(response, await pending);
        Assert.Equal(1, child.Calls);
    }

    [Theory]
    [InlineData("sotw")]
    [InlineData("delta")]
    [InlineData("fetch")]
    public async Task MissingRoutes_ExposeTheClassificationKey(string mode)
    {
        var mux = new MuxCache(new Dictionary<string, IConfigWatcher>(), _ => "missing/sotw", _ => "missing/delta");
        var failure = await Assert.ThrowsAsync<KeyNotFoundException>(() => InvokeAsync(mux, mode));
        Assert.Contains(mode == "delta" ? "missing/delta" : "missing/sotw", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sotw", false)]
    [InlineData("delta", false)]
    [InlineData("fetch", false)]
    [InlineData("sotw", true)]
    [InlineData("delta", true)]
    [InlineData("fetch", true)]
    public async Task ClassifierAndChildFailures_ArePreserved(string mode, bool classifierFailure)
    {
        var failure = new InvalidOperationException("application failure");
        var child = new MuxProbeCache { Failure = failure };
        var mux = new MuxCache(new Dictionary<string, IConfigWatcher> { ["child"] = child },
            _ => classifierFailure ? throw failure : "child", _ => classifierFailure ? throw failure : "child");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(mux, mode)));
        Assert.Equal(classifierFailure ? 0 : 1, child.Calls);
    }

    [Fact]
    public async Task FetchTaskFailure_IsReturnedWithoutWrapping()
    {
        var failure = new InvalidOperationException("asynchronous child failure");
        var child = new MuxProbeCache();
        child.FetchCompletion.SetException(failure);
        var mux = new MuxCache(new Dictionary<string, IConfigWatcher> { ["child"] = child }, _ => "child", _ => "child");
        Task<ISotwResponse> result = mux.FetchAsync(Request(ResourceTypeUrls.Route));
        Assert.Same(child.FetchCompletion.Task, result);
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => result));
    }

    [Fact]
    public async Task UnsupportedFetch_RemainsVisibleForLinearAndWatcherOnlyChildren()
    {
        var linear = new LinearCache(ResourceTypeUrls.Route);
        var watcher = new RejectingWatcher(new InvalidOperationException("A watch must not be created by Fetch"));
        var mux = new MuxCache(new Dictionary<string, IConfigWatcher> { ["linear"] = linear, ["watcher"] = watcher },
            request => request.Node.Id, _ => "linear");
        var linearError = await Assert.ThrowsAsync<NotSupportedException>(() => mux.FetchAsync(Request(ResourceTypeUrls.Route, nodeId: "linear")));
        Assert.Equal("Fetch is not implemented by LinearCache.", linearError.Message);
        var watcherError = await Assert.ThrowsAsync<NotSupportedException>(() => mux.FetchAsync(Request(ResourceTypeUrls.Cluster, nodeId: "watcher")));
        Assert.Contains("watcher", watcherError.Message, StringComparison.Ordinal);
        Assert.Equal(0, linear.NumCacheWatches);
    }

    [Fact]
    public void RoutingIndex_IsCopiedAndChildrenRemainCallerOwned()
    {
        var child = new MuxProbeCache();
        var index = new Dictionary<string, IConfigWatcher> { ["Exact"] = child };
        var mux = new MuxCache(index, request => request.Node.Id, _ => "Exact");
        index.Clear();
        using var registration = mux.CreateWatch(Request(ResourceTypeUrls.Route, nodeId: "Exact"), Subscription(), Channel.CreateUnbounded<ISotwResponse>().Writer);
        Assert.Same(child.Registration, registration);
        registration.Dispose();
        Assert.Equal(0, child.Disposals);
        Assert.Throws<KeyNotFoundException>(() => mux.CreateWatch(Request(ResourceTypeUrls.Route, nodeId: "exact"), Subscription(), Channel.CreateUnbounded<ISotwResponse>().Writer));
        child.Dispose();
        Assert.Equal(1, child.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedChildren_RouteBothModesAndCancelOnlyTheSelectedWatch(bool delta)
    {
        var snapshot = new SnapshotCache();
        var linear = new LinearCache(ResourceTypeUrls.Route);
        var mux = Mixed(snapshot, linear);
        var subscription = Subscription(["a"]);
        if (delta)
        {
            using var snapshotWatch = mux.CreateDeltaWatch(DeltaRequest(ResourceTypeUrls.Cluster), subscription);
            using var linearWatch = mux.CreateDeltaWatch(DeltaRequest(ResourceTypeUrls.Route), subscription);
            Assert.Equal(1, snapshot.GetStatusInfo("node")!.NumDeltaWatches);
            Assert.Equal(1, linear.NumCacheWatches);
            linearWatch.Dispose();
            Assert.Equal(0, linear.NumCacheWatches);
            Assert.Equal(1, snapshot.GetStatusInfo("node")!.NumDeltaWatches);
            await snapshot.SetSnapshotAsync("node", Clusters("v1"));
            Assert.Equal("a", Assert.Single((await snapshotWatch.Completion).GetDeltaDiscoveryResponse().Resources).Name);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => linearWatch.Completion);
        }
        else
        {
            using var snapshotWatch = mux.CreateWatch(Request(ResourceTypeUrls.Cluster, "0"), subscription);
            using var linearWatch = mux.CreateWatch(Request(ResourceTypeUrls.Route, "0"), subscription);
            Assert.Equal(1, snapshot.GetStatusInfo("node")!.NumWatches);
            Assert.Equal(1, linear.NumCacheWatches);
            linearWatch.Dispose();
            Assert.Equal(0, linear.NumCacheWatches);
            Assert.Equal(1, snapshot.GetStatusInfo("node")!.NumWatches);
            await snapshot.SetSnapshotAsync("node", Clusters("v1"));
            Assert.Equal("a", Assert.Single((await snapshotWatch.Completion).GetDiscoveryResponse().Resources).Unpack<Cluster>().Name);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => linearWatch.Completion);
        }
        await linear.UpdateResourceAsync("a", new RouteConfiguration { Name = "a" }, TestContext.Current.CancellationToken);
        Assert.Equal(0, linear.NumCacheWatches);
        Assert.Equal(1, linear.NumResources);
    }

    [Fact]
    public async Task BlockedChild_DoesNotPreventOtherRoutesFromProgressing()
    {
        var snapshot = new SnapshotCache();
        var linear = new LinearCache(ResourceTypeUrls.Route);
        var mux = Mixed(snapshot, linear);
        var responses = Channel.CreateBounded<ISotwResponse>(1);
        var marker = new TestSotwResponse(Request(ResourceTypeUrls.Route), "marker", new Dictionary<string, string>(), default, () => new DiscoveryResponse());
        Assert.True(responses.Writer.TryWrite(marker));
        await using var registration = (IAsyncDisposable)mux.CreateWatch(Request(ResourceTypeUrls.Route, "0"), Subscription(["a"]), responses.Writer);
        Task blocked = linear.UpdateResourceAsync("a", new RouteConfiguration { Name = "a" }, TestContext.Current.CancellationToken);
        Assert.False(blocked.IsCompleted);
        using var other = mux.CreateWatch(Request(ResourceTypeUrls.Cluster), Subscription(wildcard: true));
        await snapshot.SetSnapshotAsync("node", Clusters("independent"));
        Assert.Equal("independent", (await other.Completion).ResponseVersion);
        Assert.False(blocked.IsCompleted);
        Assert.Same(marker, await responses.Reader.ReadAsync(TestContext.Current.CancellationToken));
        await blocked;
        Assert.Equal("1", (await responses.Reader.ReadAsync(TestContext.Current.CancellationToken)).ResponseVersion);
    }

    private static MuxCache Mixed(SnapshotCache snapshot, LinearCache linear) => new(
        new Dictionary<string, IConfigWatcher> { [ResourceTypeUrls.Cluster] = snapshot, [ResourceTypeUrls.Route] = linear },
        request => request.TypeUrl, request => request.TypeUrl);

    private static Snapshot Clusters(string version) => new(version, new Dictionary<string, IEnumerable<IMessage?>>
    {
        [ResourceTypeUrls.Cluster] = [new Cluster { Name = "a" }],
    });

    private static DeltaDiscoveryRequest DeltaRequest(string typeUrl) => new() { TypeUrl = typeUrl, Node = new Node { Id = "node" }, ResponseNonce = "accepted" };

    private static async Task InvokeAsync(MuxCache mux, string mode)
    {
        if (mode == "fetch")
        {
            await mux.FetchAsync(Request(ResourceTypeUrls.Route));
        }
        else if (mode == "delta")
        {
            using var watch = mux.CreateDeltaWatch(DeltaRequest(ResourceTypeUrls.Route), Subscription(), Channel.CreateUnbounded<IDeltaResponse>().Writer);
        }
        else
        {
            using var watch = mux.CreateWatch(Request(ResourceTypeUrls.Route), Subscription(), Channel.CreateUnbounded<ISotwResponse>().Writer);
        }
    }
}
