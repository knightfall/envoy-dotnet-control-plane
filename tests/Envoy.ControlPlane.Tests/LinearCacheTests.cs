using System.Threading.Channels;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Streams;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class LinearCacheTests
{
    [Fact]
    public async Task SourceCases_MatchDeclaredOutcomesAndExportActualObservations()
    {
        var inputs = LinearCacheCorpus.Cases();
        Assert.Equal(31, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var results = new List<LinearCacheResult>();
        foreach (var input in inputs)
        {
            var result = await LinearCacheCorpus.EvaluateAsync(input);
            Assert.Equal(input.ExpectedReady, result.Observed.Response is not null);
            Assert.Equal(input.ExpectedReady ? 0 : 1, result.Observed.Watches);
            if (result.Observed.Response is { } frame)
            {
                Assert.Equal(input.ExpectedVersion, frame.Version);
                Assert.Equal(input.TypeUrl, frame.TypeUrl);
                string[] names = frame.Resources.Select(resource => GetResourceName(input, resource)).Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(input.ExpectedNames, names);
                Assert.Equal(input.ExpectedRemoved, frame.Removed);
            }
            results.Add(result);
        }
        LinearCacheCorpus.WriteResults(results);
    }

    [Fact]
    public async Task InitialIndexAndResponses_RetainMessagesButOwnTheirIndexesAndCallerNames()
    {
        var message = new RouteConfiguration { Name = "wire-name" };
        var initial = new Dictionary<string, IMessage?> { ["cache-name"] = message };
        var cache = new LinearCache(ResourceTypeUrls.Route, initial, "instance/");
        initial.Clear();
        var returned = cache.GetResources();
        Assert.Same(message, returned["cache-name"]);
        returned.Clear();
        Assert.Equal(1, cache.NumResources);
        using var watch = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["cache-name"]));
        var response = await watch.Completion;
        var envelope = response.GetDeltaDiscoveryResponse();
        Assert.Equal("instance/0", envelope.SystemVersionInfo);
        var resource = Assert.Single(envelope.Resources);
        Assert.Equal("cache-name", resource.Name);
        Assert.Equal("wire-name", resource.Resource_.Unpack<RouteConfiguration>().Name);
        Assert.Equal(resource.Version, response.ReturnedResources["cache-name"]);
        Assert.Empty(envelope.Nonce);
        Assert.Null(resource.Ttl);
        Assert.Equal(default, response.Context);
        Assert.Same(envelope, response.GetDeltaDiscoveryResponse());
        Assert.Empty(cache.GetResources().Keys.Except(["cache-name"]));
    }

    [Fact]
    public async Task MixedModesAndOverlappingIndexes_DeliverOnceAndDisposeIdempotently()
    {
        var cache = new LinearCache(ResourceTypeUrls.Route);
        var sotwChannel = Channel.CreateBounded<ISotwResponse>(1);
        var deltaChannel = Channel.CreateBounded<IDeltaResponse>(1);
        var sotwSub = new SotwSubscription(["collection/a", "collection/*", "collection/nested/*"], false);
        var deltaSub = new DeltaSubscription(["*"], allowLegacyWildcard: false);
        await using var sotw = (IAsyncDisposable)cache.CreateWatch(Request(ResourceTypeUrls.Route, "0"), sotwSub, sotwChannel.Writer);
        await using var delta = (IAsyncDisposable)cache.CreateDeltaWatch(DeltaRequest(), deltaSub, deltaChannel.Writer);
        Assert.Equal(2, cache.NumCacheWatches);
        Assert.Equal(3, cache.NumWatches("collection/a"));
        Assert.Equal(3, cache.NumWatches("collection/nested/a"));
        Assert.Equal(1, cache.NumWildcardWatches);
        Assert.Equal(2, cache.NumPrefixWatches);
        await cache.UpdateResourceAsync("collection/a", new RouteConfiguration { Name = "collection/a" }, TestContext.Current.CancellationToken);
        var first = await sotwChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var second = await deltaChannel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("1", first.ReturnedResources["collection/a"]);
        Assert.NotEqual("1", second.ReturnedResources["collection/a"]);
        Assert.Equal(0, cache.NumCacheWatches);
        Assert.Equal(0, cache.NumWatches("collection/a"));
        Assert.Equal(0, cache.NumPrefixWatches);
        Assert.Equal(0, cache.NumWildcardWatches);
        await sotw.DisposeAsync();
        await delta.DisposeAsync();
        Assert.Equal(0, cache.NumCacheWatches);
        await cache.UpdateResourceAsync("collection/a", new RouteConfiguration { Name = "changed" }, TestContext.Current.CancellationToken);
        Assert.False(sotwChannel.Reader.TryRead(out _));
        Assert.False(deltaChannel.Reader.TryRead(out _));
        Assert.True(sotwChannel.Writer.TryWrite(first));
        Assert.True(deltaChannel.Writer.TryWrite(second));
    }

    [Fact]
    public async Task SerializationAndHashes_AreLazySharedAndInvalidatedOnlyForReplacedEntries()
    {
        var message = new CountingResource();
        var cache = new LinearCache(ResourceTypeUrls.Route, new Dictionary<string, IMessage?> { ["a"] = message });
        using var first = cache.CreateWatch(Request(ResourceTypeUrls.Route), new SotwSubscription());
        using var second = cache.CreateWatch(Request(ResourceTypeUrls.Route), new SotwSubscription());
        var firstResponse = await first.Completion;
        var secondResponse = await second.Completion;
        Assert.Equal(0, message.Writes);
        Assert.Single(firstResponse.GetDiscoveryResponse().Resources);
        using var delta = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]));
        var deltaResponse = await delta.Completion;
        Assert.Equal(1, message.Writes);
        Assert.Same(Assert.Single(firstResponse.GetDiscoveryResponse().Resources).Value,
            Assert.Single(secondResponse.GetDiscoveryResponse().Resources).Value);
        Assert.Same(Assert.Single(firstResponse.GetDiscoveryResponse().Resources).Value,
            Assert.Single(deltaResponse.GetDeltaDiscoveryResponse().Resources).Resource_.Value);
        await cache.UpdateResourceAsync("b", new RouteConfiguration { Name = "b" }, TestContext.Current.CancellationToken);
        Assert.Equal(1, message.Writes);
        await cache.SetResourcesAsync(new Dictionary<string, IMessage?> { ["a"] = message }, TestContext.Current.CancellationToken);
        using var replaced = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]));
        Assert.Equal(deltaResponse.ReturnedResources["a"], (await replaced.Completion).ReturnedResources["a"]);
        Assert.Equal(2, message.Writes);
    }

    [Fact]
    public async Task BlockedPublication_CancellationRetainsTheUpdateAndWatchForRetry()
    {
        var cache = new LinearCache(ResourceTypeUrls.Route);
        var channel = Channel.CreateBounded<IDeltaResponse>(1);
        var sentinel = new PassthroughDeltaResponse(DeltaRequest(), new DeltaDiscoveryResponse(), new Dictionary<string, string>());
        Assert.True(channel.Writer.TryWrite(sentinel));
        await using var registration = (IAsyncDisposable)cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["a"]), channel.Writer);
        using var cancellation = new CancellationTokenSource();
        var message = new RouteConfiguration { Name = "a" };
        Task publication = cache.UpdateResourceAsync("a", message, cancellation.Token);
        Assert.False(publication.IsCompleted);
        Assert.Same(message, cache.GetResources()["a"]);
        Assert.Equal(1, cache.NumCacheWatches);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication);
        Assert.Same(sentinel, await channel.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, cache.NumCacheWatches);
        await cache.UpdateResourceAsync("a", message, TestContext.Current.CancellationToken);
        var response = await channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("2", response.ResponseVersion);
        Assert.Equal("a", Assert.Single(response.GetDeltaDiscoveryResponse().Resources).Name);
        Assert.Equal(0, cache.NumCacheWatches);
    }

    [Fact]
    public async Task DisposalSettlesBlockedWrites_AndQueuedCancellationDoesNotInstallAnotherUpdate()
    {
        var cache = new LinearCache(ResourceTypeUrls.Route);
        var channel = Channel.CreateBounded<ISotwResponse>(1);
        var sentinel = new TestSotwResponse(Request(ResourceTypeUrls.Route), "sentinel", new Dictionary<string, string>(), default, () => new DiscoveryResponse());
        Assert.True(channel.Writer.TryWrite(sentinel));
        var registration = cache.CreateWatch(Request(ResourceTypeUrls.Route, "0"), new SotwSubscription(["a"]), channel.Writer);
        Task first = cache.UpdateResourceAsync("a", new RouteConfiguration { Name = "a" }, TestContext.Current.CancellationToken);
        Assert.False(first.IsCompleted);
        using var cancellation = new CancellationTokenSource();
        Task queued = cache.UpdateResourceAsync("b", new RouteConfiguration { Name = "b" }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.False(cache.GetResources().ContainsKey("b"));
        await ((IAsyncDisposable)registration).DisposeAsync();
        await first;
        Assert.Equal(0, cache.NumCacheWatches);
        Assert.Same(sentinel, await channel.Reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(channel.Reader.TryRead(out _));
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Route), new SotwSubscription());
        Assert.Equal("1", (await watch.Completion).ResponseVersion);
    }

    [Fact]
    public async Task InvalidSingleUpdateAndWrongWatchTypes_DoNotChangeTheCollection_FetchIsUnsupported()
    {
        var cache = new LinearCache(ResourceTypeUrls.Route);
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.UpdateResourceAsync("a", null!, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => cache.CreateWatch(Request(ResourceTypeUrls.Cluster), new SotwSubscription(), Channel.CreateUnbounded<ISotwResponse>().Writer));
        Assert.Throws<ArgumentException>(() => cache.CreateDeltaWatch(new DeltaDiscoveryRequest { TypeUrl = ResourceTypeUrls.Cluster }, new DeltaSubscription(), Channel.CreateUnbounded<IDeltaResponse>().Writer));
        Assert.Equal(0, cache.NumCacheWatches);
        Assert.Equal(0, cache.NumResources);
        using var watch = cache.CreateWatch(Request(ResourceTypeUrls.Route), new SotwSubscription());
        Assert.Equal("0", (await watch.Completion).ResponseVersion);
        await Assert.ThrowsAsync<NotSupportedException>(() => cache.FetchAsync(Request(ResourceTypeUrls.Route), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelPendingRegistration_RemovesItsIndexesAndPreservesOtherWatches()
    {
        var cache = new LinearCache(ResourceTypeUrls.Route);
        var channel = Channel.CreateBounded<ISotwResponse>(1);
        var registration = cache.CreateWatch(Request(ResourceTypeUrls.Route, "0"), new SotwSubscription(["a", "collection/*"], false), channel.Writer);
        using var other = cache.CreateDeltaWatch(DeltaRequest(), new DeltaSubscription(["other"]));
        Assert.Equal(2, cache.NumCacheWatches);
        Assert.Equal(1, cache.NumPrefixWatches);
        registration.Dispose();
        registration.Dispose();
        await ((IAsyncDisposable)registration).DisposeAsync();
        Assert.Equal(1, cache.NumCacheWatches);
        Assert.Equal(0, cache.NumWatches("a"));
        Assert.Equal(0, cache.NumPrefixWatches);
        await cache.UpdateResourceAsync("a", new RouteConfiguration { Name = "a" }, TestContext.Current.CancellationToken);
        Assert.False(channel.Reader.TryRead(out _));
        Assert.False(other.Completion.IsCompleted);
        await cache.UpdateResourceAsync("other", new RouteConfiguration { Name = "other" }, TestContext.Current.CancellationToken);
        Assert.Equal("other", Assert.Single((await other.Completion).GetDeltaDiscoveryResponse().Resources).Name);
        Assert.Equal(0, cache.NumCacheWatches);
    }

    [Theory]
    [InlineData(ResourceTypeUrls.Cluster)]
    [InlineData(ResourceTypeUrls.Listener)]
    public async Task FullStateTypes_IncludeUnchangedResourcesAndSendTheFinalRemoval(string typeUrl)
    {
        IMessage first = typeUrl == ResourceTypeUrls.Cluster ? new Cluster { Name = "a" } : new Listener { Name = "a" };
        IMessage second = typeUrl == ResourceTypeUrls.Cluster ? new Cluster { Name = "b" } : new Listener { Name = "b" };
        var cache = new LinearCache(typeUrl, new Dictionary<string, IMessage?> { ["a"] = first });
        var subscription = new SotwSubscription();
        subscription.SetReturnedResources(new Dictionary<string, string> { ["a"] = "0" });
        using var added = cache.CreateWatch(Request(typeUrl, "0"), subscription);
        await cache.UpdateResourceAsync("b", second, TestContext.Current.CancellationToken);
        var response = await added.Completion;
        Assert.Equal(2, response.GetDiscoveryResponse().Resources.Count);
        Assert.Equal("0", response.ReturnedResources["a"]);
        Assert.Equal("1", response.ReturnedResources["b"]);
        subscription.SetReturnedResources(response.ReturnedResources);
        using var removed = cache.CreateWatch(Request(typeUrl, "1"), subscription);
        await cache.SetResourcesAsync(new Dictionary<string, IMessage?>(), TestContext.Current.CancellationToken);
        var empty = await removed.Completion;
        Assert.Equal("2", empty.ResponseVersion);
        Assert.Empty(empty.GetDiscoveryResponse().Resources);
        Assert.Empty(empty.ReturnedResources);
    }

    [Fact]
    public async Task SerializationFailure_IsCachedUntilReplacement_AndDeltaCreationReportsItSynchronously()
    {
        var failure = new InvalidOperationException("cannot serialize");
        var resource = new ThrowingResource(failure);
        var cache = new LinearCache("application/custom", new Dictionary<string, IMessage?> { ["broken"] = resource });
        using var watch = cache.CreateWatch(Request("application/custom"), new SotwSubscription());
        var response = await watch.Completion;
        Assert.Equal(0, resource.Attempts);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(response.GetDiscoveryResponse));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => cache.CreateDeltaWatch(new DeltaDiscoveryRequest { TypeUrl = "application/custom" }, new DeltaSubscription(["broken"]), Channel.CreateUnbounded<IDeltaResponse>().Writer)));
        Assert.Equal(1, resource.Attempts);
        Assert.Equal(0, cache.NumCacheWatches);
        await cache.UpdateResourceAsync("broken", new RouteConfiguration { Name = "repaired" }, TestContext.Current.CancellationToken);
        using var repaired = cache.CreateDeltaWatch(new DeltaDiscoveryRequest { TypeUrl = "application/custom" }, new DeltaSubscription(["broken"]));
        Assert.Equal("broken", Assert.Single((await repaired.Completion).GetDeltaDiscoveryResponse().Resources).Name);
    }

    private static DeltaDiscoveryRequest DeltaRequest() => new() { TypeUrl = ResourceTypeUrls.Route, ResponseNonce = "accepted" };

    private static string GetResourceName(LinearCacheInput input, DeltaCacheResource resource)
    {
        if (input.Delta)
        {
            return resource.Name;
        }
        byte[] payload = Convert.FromBase64String(resource.Data);
        return input.TypeUrl == ResourceTypeUrls.Cluster ? Cluster.Parser.ParseFrom(payload).Name : RouteConfiguration.Parser.ParseFrom(payload).Name;
    }

    private sealed class CountingResource : IMessage
    {
        private readonly RouteConfiguration inner = new() { Name = "a" };
        public int Writes { get; private set; }
        public MessageDescriptor Descriptor => RouteConfiguration.Descriptor;
        public int CalculateSize() => inner.CalculateSize();
        public void WriteTo(CodedOutputStream output)
        {
            Writes++;
            inner.WriteTo(output);
        }
        public void MergeFrom(CodedInputStream input) => throw new NotSupportedException();
    }
}
