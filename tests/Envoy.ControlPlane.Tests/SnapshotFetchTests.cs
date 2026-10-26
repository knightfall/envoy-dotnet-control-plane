using Envoy.Config.Cluster.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Google.Protobuf;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.SnapshotCacheFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SnapshotFetchTests
{
    [Fact]
    public async Task SourceCases_MatchDeclaredOutcomesAndExportActualObservations()
    {
        var inputs = FetchCorpus.Cases();
        Assert.Equal(14, inputs.Length);
        Assert.Equal(14, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var results = new List<FetchResult>();
        foreach (var input in inputs) results.Add(await FetchCorpus.EvaluateAsync(input));
        FetchCorpus.Write(results);
    }

    [Theory]
    [InlineData("", "a,b")]
    [InlineData("a", "a")]
    [InlineData("a,a,missing,b", "a,a,b")]
    [InlineData("*", "")]
    [InlineData("collection/*", "")]
    public async Task Selection_UsesAllOrLiteralNamesAndRetainsDuplicates(string names, string expected)
    {
        var clock = new TestTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = new SnapshotCache(ads: true, nodeHasher: new FixedNodeHasher("mapped"), timeProvider: clock);
        await cache.SetSnapshotAsync("mapped", new Snapshot("opaque/v1", new Dictionary<string, IEnumerable<IMessage?>>
        {
            [ResourceTypeUrls.Cluster] = [new Cluster { Name = "a" }, new Cluster { Name = "b" }],
        }));
        var request = Request(ResourceTypeUrls.Cluster, nodeId: "ignored", names: names.Length == 0 ? [] : names.Split(','));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var context = new ResponseContext(stop.Token, new object());
        var raw = await cache.FetchAsync(request, context);
        var response = raw.GetDiscoveryResponse();
        Assert.Same(request, raw.Request);
        Assert.Equal(context, raw.Context);
        Assert.Empty(raw.ReturnedResources);
        Assert.Equal("opaque/v1", raw.ResponseVersion);
        Assert.Equal("opaque/v1", response.VersionInfo);
        Assert.Equal(ResourceTypeUrls.Cluster, response.TypeUrl);
        Assert.Empty(response.Nonce);
        Assert.Equal(expected.Length == 0 ? [] : expected.Split(','), response.Resources.Select(item => item.Unpack<Cluster>().Name).Order(StringComparer.Ordinal));
        Assert.Empty(cache.GetStatusKeys());
        Assert.Equal(0, clock.Reads);
        Assert.Same(response, raw.GetDiscoveryResponse());
    }

    [Fact]
    public async Task MissingNodeAndEqualVersion_HaveDistinctErrorsWithoutCreatingStatus()
    {
        var cache = new SnapshotCache();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => cache.FetchAsync(Request(ResourceTypeUrls.Cluster)));
        await cache.SetSnapshotAsync("node", new Snapshot("opaque", new Dictionary<string, IEnumerable<IMessage?>> { [ResourceTypeUrls.Cluster] = [] }));
        await Assert.ThrowsAsync<SkipFetchException>(() => cache.FetchAsync(Request(ResourceTypeUrls.Cluster, "opaque")));
        Assert.Empty((await cache.FetchAsync(Request(ResourceTypeUrls.Cluster))).GetDiscoveryResponse().Resources);
        await Assert.ThrowsAsync<SkipFetchException>(() => cache.FetchAsync(Request("absent/type")));
        var unknown = await cache.FetchAsync(Request("absent/type", "different"));
        Assert.Empty(unknown.ResponseVersion);
        Assert.Empty(unknown.GetDiscoveryResponse().Resources);
        Assert.Empty(cache.GetStatusKeys());
    }

    [Fact]
    public async Task Materialization_IsLazyAndRetainsTheFetchedSnapshotAfterReplacement()
    {
        var failure = new InvalidOperationException("serialization");
        var resource = new ThrowingResource(failure);
        var cache = new SnapshotCache();
        await cache.SetSnapshotAsync("node", new SingleResourceSnapshot("custom/type", new ResourceEntry(resource, TimeSpan.Zero)));
        var response = await cache.FetchAsync(Request("custom/type"));
        Assert.Equal(0, resource.Attempts);
        await cache.SetSnapshotAsync("node", new Snapshot("replacement", new Dictionary<string, IEnumerable<IMessage?>>()));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => response.GetDiscoveryResponse()));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => response.GetDiscoveryResponse()));
        Assert.Equal(1, resource.Attempts);
        Assert.Equal("application/v1", response.ResponseVersion);
        Assert.Empty(cache.GetStatusKeys());
    }
}
