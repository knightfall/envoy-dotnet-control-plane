using System.Security.Cryptography;
using System.Threading.Channels;
using Envoy.Config.Core.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using DiscoveryResource = Envoy.Service.Discovery.V3.Resource;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class DeltaCacheTests
{
    [Fact]
    public async Task SourceCacheCases_MatchDeclaredOutcomesAndExportActualObservations()
    {
        var inputs = DeltaCacheCorpus.Cases();
        Assert.Equal(14, inputs.Length);
        Assert.Equal(14, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var results = new List<DeltaCacheResult>();
        foreach (var input in inputs)
        {
            var result = await DeltaCacheCorpus.EvaluateAsync(input);
            Assert.Equal(input.ExpectedReady, result.Observed.Response is not null);
            Assert.Equal(input.ExpectedReady ? 0 : 1, result.Observed.Watches);
            Assert.Equal(input.Before is not null || input.After is not null, result.Observed.VersionMapBuilt);
            if (result.Observed.Response is { } response)
            {
                Assert.Equal(input.ExpectedAdded, response.Resources.Select(resource => resource.Name));
                Assert.Equal(input.ExpectedRemoved, response.Removed);
                Assert.Equal(input.ExpectedVersion, response.Version);
                Assert.Equal(ResourceTypeUrls.Route, response.TypeUrl);
                Assert.All(response.Resources, resource => Assert.False(resource.HasTtl));
            }
            results.Add(result);
        }
        DeltaCacheCorpus.WriteResults(results.ToArray());
    }

    [Fact]
    public async Task InitialResponse_ConstructsHashesLazilyAndPreservesTheEnvelope()
    {
        var cache = new SnapshotCache();
        var snapshot = Routes("system/v1", new RouteConfiguration { Name = "a" });
        await cache.SetSnapshotAsync("node", snapshot);
        Assert.Null(snapshot.GetVersionMap(ResourceTypeUrls.Route));
        var request = DeltaRequest();
        using var watch = cache.CreateDeltaWatch(request, new DeltaSubscription(["a"]));
        var response = await watch.Completion;
        var envelope = response.GetDeltaDiscoveryResponse();
        var resource = Assert.Single(envelope.Resources);
        Assert.Same(request, response.Request);
        Assert.Equal("system/v1", response.ResponseVersion);
        Assert.Equal("system/v1", envelope.SystemVersionInfo);
        Assert.Equal(ResourceTypeUrls.Route, envelope.TypeUrl);
        Assert.Equal("a", resource.Name);
        Assert.Equal("a", resource.Resource_.Unpack<RouteConfiguration>().Name);
        // Independent wire bytes for RouteConfiguration { name: "a" }.
        string hash = Convert.ToHexStringLower(SHA256.HashData(new byte[] { 10, 1, 97 }));
        Assert.Equal(hash, resource.Version);
        Assert.Equal(hash, response.ReturnedResources["a"]);
        Assert.Equal(hash, snapshot.GetVersionMap(ResourceTypeUrls.Route)!["a"]);
        Assert.Empty(envelope.Nonce);
        Assert.Empty(envelope.RemovedResources);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumDeltaWatches);
        Assert.Same(envelope, response.GetDeltaDiscoveryResponse());
    }

    [Fact]
    public async Task UnchangedVersionsRemainPending_ChangedPayloadAndRemovalRespondWithoutTypeVersionChange()
    {
        var cache = new SnapshotCache();
        var initial = Routes("same", new RouteConfiguration { Name = "a" });
        initial.ConstructVersionMap();
        var known = initial.GetVersionMap(ResourceTypeUrls.Route)!;
        var subscription = new DeltaSubscription(["a"], initialResourceVersions: known);
        await cache.SetSnapshotAsync("node", initial);
        using var watch = cache.CreateDeltaWatch(DeltaRequest("accepted"), subscription);
        Assert.False(watch.Completion.IsCompleted);
        await cache.SetSnapshotAsync("node", Routes("same", new RouteConfiguration { Name = "a", IgnorePortInHostMatching = true }));
        var changed = await watch.Completion;
        Assert.Equal("same", changed.ResponseVersion);
        var resource = Assert.Single(changed.GetDeltaDiscoveryResponse().Resources);
        Assert.NotEqual(known["a"], resource.Version);
        Assert.True(resource.Resource_.Unpack<RouteConfiguration>().IgnorePortInHostMatching);
        Assert.Equal(known["a"], subscription.ReturnedResources["a"]);
        subscription.SetReturnedResources(changed.ReturnedResources);
        using var removedWatch = cache.CreateDeltaWatch(DeltaRequest("next"), subscription);
        Assert.False(removedWatch.Completion.IsCompleted);
        await cache.SetSnapshotAsync("node", Routes("same"));
        var removed = await removedWatch.Completion;
        Assert.Equal("a", Assert.Single(removed.GetDeltaDiscoveryResponse().RemovedResources));
        Assert.Empty(removed.GetDeltaDiscoveryResponse().Resources);
        Assert.Empty(removed.ReturnedResources);
    }

    [Theory]
    [InlineData("")]
    [InlineData("accepted")]
    public async Task EmptyWildcardResponse_IsOnlyForcedDuringCreationWithAnEmptyNonce(string nonce)
    {
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", Routes("v1"));
        using var watch = cache.CreateDeltaWatch(DeltaRequest(nonce), new DeltaSubscription());
        if (nonce.Length == 0)
        {
            var response = await watch.Completion;
            Assert.Equal("v1", response.ResponseVersion);
            Assert.Empty(response.GetDeltaDiscoveryResponse().Resources);
            Assert.Empty(response.GetDeltaDiscoveryResponse().RemovedResources);
            Assert.Empty(response.ReturnedResources);
        }
        else
        {
            Assert.False(watch.Completion.IsCompleted);
            await cache.SetSnapshotAsync("node", Routes("v2"));
            Assert.False(watch.Completion.IsCompleted);
            await cache.SetSnapshotAsync("node", Routes("v3", new RouteConfiguration { Name = "a" }));
            Assert.Equal("a", Assert.Single((await watch.Completion).GetDeltaDiscoveryResponse().Resources).Name);
        }
    }

    [Fact]
    public async Task DelayedEmptyWildcardPublication_DoesNotForceAnEmptyReply()
    {
        var cache = new SnapshotCache();
        using var watch = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription());
        await cache.SetSnapshotAsync("node", Routes("v1"));
        Assert.False(watch.Completion.IsCompleted);
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumDeltaWatches);
    }

    [Fact]
    public async Task PrefixOnlySelection_PreservesThePinnedSnapshotCacheLimitation()
    {
        var cache = new SnapshotCache(ads: true);
        var snapshot = Routes("v1", new RouteConfiguration { Name = "collection/a" });
        await cache.SetSnapshotAsync("node", snapshot);
        var subscription = new DeltaSubscription(["collection/*"], allowLegacyWildcard: false);
        using var pending = cache.CreateDeltaWatch(DeltaRequest("accepted"), subscription);
        Assert.False(pending.Completion.IsCompleted);
        pending.Dispose();
        subscription.SetReturnedResources(snapshot.GetVersionMap(ResourceTypeUrls.Route)!);
        using var removal = cache.CreateDeltaWatch(DeltaRequest("accepted"), subscription);
        var response = await removal.Completion;
        Assert.Equal("collection/a", Assert.Single(response.GetDeltaDiscoveryResponse().RemovedResources));
        Assert.Empty(response.GetDeltaDiscoveryResponse().Resources);
        Assert.Empty(response.ReturnedResources);
        Assert.Contains("collection/", subscription.SubscribedPrefixes);
    }

    [Fact]
    public async Task WildcardExplicitNameRemoval_ResendsAnUnchangedResource()
    {
        var cache = new SnapshotCache();
        var snapshot = Routes("v1", new RouteConfiguration { Name = "a" });
        snapshot.ConstructVersionMap();
        await cache.SetSnapshotAsync("node", snapshot);
        var subscription = new DeltaSubscription(["*", "a"], initialResourceVersions: snapshot.GetVersionMap(ResourceTypeUrls.Route));
        subscription.UpdateResourceSubscriptions([], ["a"]);
        using var watch = cache.CreateDeltaWatch(DeltaRequest("accepted"), subscription);
        var response = await watch.Completion;
        Assert.Equal("a", Assert.Single(response.GetDeltaDiscoveryResponse().Resources).Name);
        Assert.Equal(snapshot.GetVersionMap(ResourceTypeUrls.Route)!["a"], response.ReturnedResources["a"]);
        Assert.Empty(response.GetDeltaDiscoveryResponse().RemovedResources);
        Assert.Equal("", subscription.ReturnedResources["a"]);
    }

    [Fact]
    public async Task RawDeltaEnvelope_OmitsTtlAtThePinnedBaseline()
    {
        var cache = new SnapshotCache();
        var snapshot = new Snapshot("v1", new Dictionary<string, IEnumerable<ResourceEntry>>
        {
            [ResourceTypeUrls.Route] = [new(new RouteConfiguration { Name = "a" }, TimeSpan.FromSeconds(30))],
        });
        await cache.SetSnapshotAsync("node", snapshot);
        using var watch = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]));
        var resource = Assert.Single((await watch.Completion).GetDeltaDiscoveryResponse().Resources);
        Assert.Null(resource.Ttl);
        Assert.Equal(ResourceTypeUrls.Route, resource.Resource_.TypeUrl);
        Assert.Equal("a", resource.Resource_.Unpack<RouteConfiguration>().Name);
    }

    [Fact]
    public async Task CustomSnapshot_ResponseMaterializationIsLazyAndCachesItsFailure()
    {
        var failure = new InvalidOperationException("cannot serialize");
        var resource = new ThrowingResource(failure);
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", new SingleResourceSnapshot("application/custom", new ResourceEntry(resource)));
        using var watch = cache.CreateDeltaWatch(new DeltaDiscoveryRequest { Node = new Node { Id = "node" }, TypeUrl = "application/custom" },
            new DeltaSubscription(["broken"]));
        var response = await watch.Completion;
        Assert.Equal(0, resource.Attempts);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(response.GetDeltaDiscoveryResponse));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(response.GetDeltaDiscoveryResponse));
        Assert.Equal(1, resource.Attempts);
    }

    [Fact]
    public void DeltaStatus_SharesNodeIdentityButTracksItsOwnClockAndCount()
    {
        var clock = new TestTimeProvider(DateTimeOffset.Parse("2026-10-26T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var cache = new SnapshotCache(nodeHasher: new FixedNodeHasher("partition"), timeProvider: clock);
        var request = DeltaRequest();
        using var delta = cache.CreateDeltaWatch(request, new DeltaSubscription());
        var status = cache.GetStatusInfo("partition")!;
        Assert.Same(request.Node, status.Node);
        Assert.Equal(clock.Current, status.LastDeltaWatchRequestTime);
        Assert.Equal(default, status.LastWatchRequestTime);
        Assert.Equal(1, status.NumDeltaWatches);
        clock.Current += TimeSpan.FromMinutes(1);
        using var sotw = cache.CreateWatch(Request(ResourceTypeUrls.Route, nodeId: "other"), Subscription());
        Assert.Same(request.Node, status.Node);
        Assert.Equal(clock.Current, status.LastWatchRequestTime);
        Assert.Equal(clock.Current - TimeSpan.FromMinutes(1), status.LastDeltaWatchRequestTime);
        Assert.Equal(1, status.NumWatches);
        delta.Dispose();
        Assert.Equal(0, status.NumDeltaWatches);
        Assert.Equal(1, status.NumWatches);
    }

    [Fact]
    public async Task BlockedPublicationCancellation_RetainsAnEligibleWatchAndAllowsRetry()
    {
        var cache = new SnapshotCache();
        var channel = Channel.CreateBounded<IDeltaResponse>(1);
        var sentinel = new PassthroughDeltaResponse(DeltaRequest(), new DeltaDiscoveryResponse(), new Dictionary<string, string>());
        Assert.True(channel.Writer.TryWrite(sentinel));
        await using var registration = (IAsyncDisposable)cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]), channel.Writer);
        using var cancellation = new CancellationTokenSource();
        var snapshot = Routes("v1", new RouteConfiguration { Name = "a" });
        Task publication = cache.SetSnapshotAsync("node", snapshot, new ResponseContext(cancellation.Token, "publisher"));
        Assert.False(publication.IsCompleted);
        Assert.Same(snapshot, cache.GetSnapshot("node"));
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumDeltaWatches);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication);
        Assert.Same(sentinel, await channel.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, cache.GetStatusInfo("node")!.NumDeltaWatches);
        await cache.SetSnapshotAsync("node", snapshot, new ResponseContext(default, "retry"));
        var response = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("retry", response.Context.State);
        Assert.Equal("a", Assert.Single(response.GetDeltaDiscoveryResponse().Resources).Name);
        Assert.Equal(0, cache.GetStatusInfo("node")!.NumDeltaWatches);
        Assert.True(channel.Writer.TryWrite(sentinel));
    }

    [Fact]
    public void PassthroughEnvelope_PreservesCallerMetadataAndPayloadWithoutInterpretation()
    {
        var request = DeltaRequest();
        var envelope = new DeltaDiscoveryResponse { TypeUrl = "application/custom", SystemVersionInfo = "opaque", Nonce = "provided" };
        envelope.Resources.Add(new DiscoveryResource
        {
            Name = "custom", Resource_ = new Any { TypeUrl = "application/custom", Value = ByteString.CopyFromUtf8("opaque bytes") }, Version = "custom/version",
        });
        envelope.RemovedResources.Add("old");
        var returned = new Dictionary<string, string> { ["custom"] = "custom/version", ["unchanged"] = "retained" };
        var context = new ResponseContext(default, new object());
        var response = new PassthroughDeltaResponse(request, envelope, returned, context);
        Assert.Same(request, response.Request);
        Assert.Same(envelope, response.GetDeltaDiscoveryResponse());
        Assert.Same(returned, response.ReturnedResources);
        Assert.Equal("opaque", response.ResponseVersion);
        Assert.Equal("retained", response.ReturnedResources["unchanged"]);
        Assert.Equal(context, response.Context);
    }

    private static DeltaDiscoveryRequest DeltaRequest(string nonce = "") => new()
    {
        TypeUrl = ResourceTypeUrls.Route, Node = new Node { Id = "node" }, ResponseNonce = nonce,
    };

    private static Snapshot Routes(string version, params RouteConfiguration[] routes) =>
        new(version, new Dictionary<string, IEnumerable<IMessage?>> { [ResourceTypeUrls.Route] = routes });
}
